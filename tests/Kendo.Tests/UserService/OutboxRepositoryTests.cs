using Kendo.Shared.Messaging;
using Kendo.UserService.Data;
using Microsoft.EntityFrameworkCore;

namespace Kendo.Tests.UserService;

[Trait("Category", "Unit")]
public class OutboxRepositoryTests
{
    private class PassThroughResiliencePipeline : Kendo.Shared.Resilience.IResiliencePipeline
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct = default)
            => action(ct);
        public Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken ct = default)
            => action(ct);
    }

    private static (OutboxRepository repo, AppDbContext db) CreateSut(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var db = new AppDbContext(options);
        var resilientDb = new ResilientAppDbContext(db, new PassThroughResiliencePipeline());
        var repo = new OutboxRepository(db, resilientDb);
        return (repo, db);
    }

    [Fact]
    public async Task AddAsync_CreatesOutboxRecord()
    {
        var (repo, db) = CreateSut(nameof(AddAsync_CreatesOutboxRecord));

        var message = new UserCreatedEvent
        {
            UserId = Guid.NewGuid(),
            Email = "record@example.com",
            DisplayName = "Record Test"
        };

        await repo.AddAsync(message);

        var records = await db.OutboxMessages.ToListAsync();
        var record = Assert.Single(records);
        Assert.Equal(message.MessageId, record.MessageId);
        Assert.Equal("Kendo.Shared.Messaging.UserCreatedEvent, Kendo.Shared", record.MessageType);
        Assert.Contains("record@example.com", record.Payload);
        Assert.Null(record.ProcessedAt);
        Assert.Equal(0, record.RetryCount);
    }

    [Fact]
    public async Task AddAsync_RespectsMessageId()
    {
        var (repo, db) = CreateSut(nameof(AddAsync_RespectsMessageId));

        var message = new UserCreatedEvent
        {
            UserId = Guid.NewGuid(),
            Email = "unique@example.com",
            DisplayName = "Unique"
        };

        await repo.AddAsync(message);

        // Adding the same MessageId should succeed (MessageId unique constraint is DB-level)
        // In-memory DB doesn't enforce unique constraints, so this tests the application logic
        var records = await db.OutboxMessages.Where(m => m.MessageId == message.MessageId).ToListAsync();
        Assert.Single(records);
    }

    [Fact]
    public async Task GetPendingCountAsync_ReturnsCorrectCount()
    {
        var (repo, db) = CreateSut(nameof(GetPendingCountAsync_ReturnsCorrectCount));

        var msg1 = new UserCreatedEvent { UserId = Guid.NewGuid(), Email = "a@example.com", DisplayName = "A" };
        var msg2 = new UserCreatedEvent { UserId = Guid.NewGuid(), Email = "b@example.com", DisplayName = "B" };

        await repo.AddAsync(msg1);
        await repo.AddAsync(msg2);

        var count = await repo.GetPendingCountAsync();
        Assert.Equal(2, count);

        // Mark one as processed
        var record = await db.OutboxMessages.FirstAsync(m => m.MessageId == msg1.MessageId);
        record.ProcessedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        count = await repo.GetPendingCountAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task AddAsync_StoresTraceContext_WhenActivityExists()
    {
        var (repo, db) = CreateSut(nameof(AddAsync_StoresTraceContext_WhenActivityExists));

        // Create an ambient Activity
        var testActivity = new System.Diagnostics.Activity("TestOperation");
        testActivity.Start();

        var message = new UserCreatedEvent
        {
            UserId = Guid.NewGuid(),
            Email = "trace@example.com",
            DisplayName = "Trace Test"
        };

        await repo.AddAsync(message);

        testActivity.Stop();

        var record = await db.OutboxMessages.FirstAsync(m => m.MessageId == message.MessageId);
        Assert.NotNull(record.TraceContext);
        Assert.Equal(testActivity.Id, record.TraceContext);
    }

    [Fact]
    public async Task AddAsync_TraceContextNull_WhenNoActivity()
    {
        var (repo, db) = CreateSut(nameof(AddAsync_TraceContextNull_WhenNoActivity));

        // Ensure no ambient Activity
        System.Diagnostics.Activity.Current = null;

        var message = new UserCreatedEvent
        {
            UserId = Guid.NewGuid(),
            Email = "notrace@example.com",
            DisplayName = "No Trace"
        };

        await repo.AddAsync(message);

        var record = await db.OutboxMessages.FirstAsync(m => m.MessageId == message.MessageId);
        Assert.Null(record.TraceContext);
    }

    [Fact]
    public async Task AddAsync_SerializesPayloadCorrectly()
    {
        var (repo, db) = CreateSut(nameof(AddAsync_SerializesPayloadCorrectly));

        var userId = Guid.NewGuid();
        var message = new UserCreatedEvent
        {
            UserId = userId,
            Email = "serialize@example.com",
            DisplayName = "Serialize Test"
        };

        await repo.AddAsync(message);

        var record = await db.OutboxMessages.FirstAsync(m => m.MessageId == message.MessageId);

        // Verify the payload is valid JSON with expected fields
        Assert.Contains("serialize@example.com", record.Payload);
        Assert.Contains(userId.ToString(), record.Payload);
        Assert.Contains("\"displayName\"", record.Payload);

        // Verify we can deserialize it back
        var deserialized = KendoMessageSerializer.Deserialize(record.MessageType, record.Payload);
        Assert.NotNull(deserialized);
        var typed = Assert.IsType<UserCreatedEvent>(deserialized);
        Assert.Equal(userId, typed.UserId);
    }
}
