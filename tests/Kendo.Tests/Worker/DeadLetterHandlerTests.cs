using Kendo.Shared.Messaging;
using Kendo.Worker.Data;
using Kendo.Worker.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kendo.Tests.Worker;

[Trait("Category", "Unit")]
public class DeadLetterHandlerTests
{
    private static (DeadLetterHandler handler, WorkerDbContext db) CreateSut(string dbName)
    {
        var options = new DbContextOptionsBuilder<WorkerDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var db = new WorkerDbContext(options);
        var logger = NullLogger<DeadLetterHandler>.Instance;
        var handler = new DeadLetterHandler(db, logger);
        return (handler, db);
    }

    [Fact]
    public async Task Handle_ValidDlqMessage_PersistsDlqRecord()
    {
        var (handler, db) = CreateSut(nameof(Handle_ValidDlqMessage_PersistsDlqRecord));
        var originalMessageId = Guid.NewGuid();

        var message = new DeadLetteredMessage
        {
            MessageId = Guid.NewGuid(),
            OriginalMessageId = originalMessageId,
            OriginalMessageType = "UserCreatedEvent",
            DeadLetterReason = "MaxDeliveryCountExceeded",
            DeadLetterErrorDescription = "Message processing failed after 10 retries",
            DeliveryCount = 10,
            EnqueuedTime = DateTimeOffset.UtcNow.AddMinutes(-30)
        };

        await handler.Handle(message);

        var record = await db.DlqRecords.FirstAsync(r => r.OriginalMessageId == originalMessageId);
        Assert.Equal("UserCreatedEvent", record.OriginalMessageType);
        Assert.Equal("MaxDeliveryCountExceeded", record.DeadLetterReason);
        Assert.Equal("Message processing failed after 10 retries", record.DeadLetterErrorDescription);
        Assert.Equal(10, record.DeliveryCount);
        Assert.NotNull(record.EnqueuedTime);
        Assert.False(record.Alerted);
        Assert.NotEqual(default, record.DetectedAt);
    }

    [Fact]
    public async Task Handle_DlqMessageWithoutErrorDescription_PersistsNullDescription()
    {
        var (handler, db) = CreateSut(nameof(Handle_DlqMessageWithoutErrorDescription_PersistsNullDescription));
        var originalMessageId = Guid.NewGuid();

        var message = new DeadLetteredMessage
        {
            MessageId = Guid.NewGuid(),
            OriginalMessageId = originalMessageId,
            OriginalMessageType = "UserCreatedEvent",
            DeadLetterReason = "SessionHasLocked",
            DeadLetterErrorDescription = null,
            DeliveryCount = 3,
            EnqueuedTime = null
        };

        await handler.Handle(message);

        var record = await db.DlqRecords.FirstAsync(r => r.OriginalMessageId == originalMessageId);
        Assert.Null(record.DeadLetterErrorDescription);
        Assert.Null(record.EnqueuedTime);
    }

    [Fact]
    public async Task Handle_MultipleDlqMessages_PersistsAll()
    {
        var (handler, db) = CreateSut(nameof(Handle_MultipleDlqMessages_PersistsAll));

        var msg1 = new DeadLetteredMessage
        {
            MessageId = Guid.NewGuid(),
            OriginalMessageId = Guid.NewGuid(),
            OriginalMessageType = "UserCreatedEvent",
            DeadLetterReason = "MaxDeliveryCountExceeded",
            DeliveryCount = 10
        };

        var msg2 = new DeadLetteredMessage
        {
            MessageId = Guid.NewGuid(),
            OriginalMessageId = Guid.NewGuid(),
            OriginalMessageType = "OrderPlacedEvent",
            DeadLetterReason = "ProcessingException",
            DeliveryCount = 5
        };

        await handler.Handle(msg1);
        await handler.Handle(msg2);

        var records = await db.DlqRecords.ToListAsync();
        Assert.Equal(2, records.Count);
        Assert.Contains(records, r => r.OriginalMessageType == "UserCreatedEvent");
        Assert.Contains(records, r => r.OriginalMessageType == "OrderPlacedEvent");
    }

    [Fact]
    public async Task Handle_DbFailure_DoesNotThrow()
    {
        // Use a disposed context to simulate DB failure — handler should catch and log
        var options = new DbContextOptionsBuilder<WorkerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new WorkerDbContext(options);
        await db.DisposeAsync(); // Force failure on SaveChanges

        var logger = NullLogger<DeadLetterHandler>.Instance;
        var handler = new DeadLetterHandler(db, logger);

        var message = new DeadLetteredMessage
        {
            MessageId = Guid.NewGuid(),
            OriginalMessageId = Guid.NewGuid(),
            OriginalMessageType = "UserCreatedEvent",
            DeadLetterReason = "Test",
            DeliveryCount = 1
        };

        // Should not throw — handler is fail-safe
        var ex = await Record.ExceptionAsync(() => handler.Handle(message));
        Assert.Null(ex);
    }
}
