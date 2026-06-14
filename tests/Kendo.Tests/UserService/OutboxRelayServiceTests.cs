using Kendo.Shared.Messaging;
using Kendo.UserService.Data;
using Kendo.UserService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Rebus.Bus;

namespace Kendo.Tests.UserService;

[Trait("Category", "Unit")]
public class OutboxRelayServiceTests
{
    private class PassThroughResiliencePipeline : Kendo.Shared.Resilience.IResiliencePipeline
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct = default)
            => action(ct);
        public Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken ct = default)
            => action(ct);
    }

    /// <summary>
    /// Creates an AppDbContext with in-memory database and seeds outbox messages.
    /// Returns an IServiceScopeFactory that resolves the seeded context.
    /// </summary>
    private static (IServiceScopeFactory scopeFactory, AppDbContext db, Mock<IBus> busMock) 
        CreateRelaySut(string dbName, bool withBus = true)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var db = new AppDbContext(options);
        var resilientDb = new ResilientAppDbContext(db, new PassThroughResiliencePipeline());

        // Set up DI scope
        var services = new ServiceCollection();
        services.AddSingleton<AppDbContext>(_ => db);
        services.AddSingleton<ResilientAppDbContext>(_ => resilientDb);

        var busMock = new Mock<IBus>();
        if (withBus)
        {
            services.AddSingleton<IBus>(_ => busMock.Object);
        }
        else
        {
            services.AddSingleton<IBus>(_ => null!);
        }

        var serviceProvider = services.BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        return (scopeFactory, db, busMock);
    }

    private static IConfiguration CreateConfig(int pollingInterval = 1, int batchSize = 20, int maxRetries = 5)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Relays__Outbox__PollingIntervalSeconds"] = pollingInterval.ToString(),
                ["Relays__Outbox__BatchSize"] = batchSize.ToString(),
                ["Relays__Outbox__MaxRetries"] = maxRetries.ToString()
            })
            .Build();
    }

    [Fact]
    public async Task ExecuteAsync_PublishesPendingMessages()
    {
        var (scopeFactory, db, busMock) = CreateRelaySut(nameof(ExecuteAsync_PublishesPendingMessages));

        // Seed a pending outbox message
        var message = new UserCreatedEvent
        {
            UserId = Guid.NewGuid(),
            Email = "relay@example.com",
            DisplayName = "Relay Test"
        };
        var outboxRecord = new OutboxMessage
        {
            MessageId = message.MessageId,
            MessageType = KendoMessageSerializer.GetMessageType(message),
            Payload = KendoMessageSerializer.Serialize(message),
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.OutboxMessages.Add(outboxRecord);
        await db.SaveChangesAsync();

        var relay = new OutboxRelayService(scopeFactory, CreateConfig(), NullLogger<OutboxRelayService>.Instance);

        // Run one batch cycle
        await relay.ProcessBatchAsync(CancellationToken.None);

        // Verify the message was published
        busMock.Verify(b => b.Send(It.Is<UserCreatedEvent>(e => e.Email == "relay@example.com"), It.IsAny<IDictionary<string, string>>()), Times.Once);

        // Verify the outbox record was marked as processed
        var processed = await db.OutboxMessages.FirstAsync(m => m.MessageId == message.MessageId);
        Assert.NotNull(processed.ProcessedAt);
    }

    [Fact]
    public async Task ExecuteAsync_PassesTraceparentHeader()
    {
        var (scopeFactory, db, busMock) = CreateRelaySut(nameof(ExecuteAsync_PassesTraceparentHeader));

        // Seed a pending outbox message with TraceContext
        var traceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";
        var message = new UserCreatedEvent
        {
            UserId = Guid.NewGuid(),
            Email = "traceheader@example.com",
            DisplayName = "Trace Header"
        };
        db.OutboxMessages.Add(new OutboxMessage
        {
            MessageId = message.MessageId,
            MessageType = KendoMessageSerializer.GetMessageType(message),
            Payload = KendoMessageSerializer.Serialize(message),
            CreatedAt = DateTimeOffset.UtcNow,
            TraceContext = traceParent
        });
        await db.SaveChangesAsync();

        var relay = new OutboxRelayService(scopeFactory, CreateConfig(), NullLogger<OutboxRelayService>.Instance);
        await relay.ProcessBatchAsync(CancellationToken.None);

        // Verify the message was published with traceparent header
        busMock.Verify(b => b.Send(
            It.Is<UserCreatedEvent>(e => e.Email == "traceheader@example.com"),
            It.Is<IDictionary<string, string>>(h =>
                h.ContainsKey("traceparent") && h["traceparent"] == traceParent)), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsTraceparentHeader_WhenTraceContextNull()
    {
        var (scopeFactory, db, busMock) = CreateRelaySut(nameof(ExecuteAsync_SkipsTraceparentHeader_WhenTraceContextNull));

        // Seed a pending outbox message WITHOUT TraceContext (null)
        var message = new UserCreatedEvent
        {
            UserId = Guid.NewGuid(),
            Email = "notraceheader@example.com",
            DisplayName = "No Trace Header"
        };
        db.OutboxMessages.Add(new OutboxMessage
        {
            MessageId = message.MessageId,
            MessageType = KendoMessageSerializer.GetMessageType(message),
            Payload = KendoMessageSerializer.Serialize(message),
            CreatedAt = DateTimeOffset.UtcNow,
            TraceContext = null
        });
        await db.SaveChangesAsync();

        var relay = new OutboxRelayService(scopeFactory, CreateConfig(), NullLogger<OutboxRelayService>.Instance);
        await relay.ProcessBatchAsync(CancellationToken.None);

        // Verify the message was published WITHOUT traceparent header
        busMock.Verify(b => b.Send(
            It.Is<UserCreatedEvent>(e => e.Email == "notraceheader@example.com"),
            It.Is<IDictionary<string, string>>(h => !h.ContainsKey("traceparent"))), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsWhenNoBus()
    {
        var (scopeFactory, db, _) = CreateRelaySut(nameof(ExecuteAsync_SkipsWhenNoBus), withBus: false);

        var message = new UserCreatedEvent { UserId = Guid.NewGuid(), Email = "nobus@example.com", DisplayName = "No Bus" };
        db.OutboxMessages.Add(new OutboxMessage
        {
            MessageId = message.MessageId,
            MessageType = KendoMessageSerializer.GetMessageType(message),
            Payload = KendoMessageSerializer.Serialize(message),
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var relay = new OutboxRelayService(scopeFactory, CreateConfig(), NullLogger<OutboxRelayService>.Instance);

        await relay.ProcessBatchAsync(CancellationToken.None);

        // Message should remain unprocessed (no bus to publish)
        var unprocessed = await db.OutboxMessages.FirstAsync(m => m.MessageId == message.MessageId);
        Assert.Null(unprocessed.ProcessedAt);
    }

    [Fact]
    public async Task ExecuteAsync_HandlesPublishFailure()
    {
        var (scopeFactory, db, busMock) = CreateRelaySut(nameof(ExecuteAsync_HandlesPublishFailure));

        var message = new UserCreatedEvent { UserId = Guid.NewGuid(), Email = "fail@example.com", DisplayName = "Fail Test" };
        db.OutboxMessages.Add(new OutboxMessage
        {
            MessageId = message.MessageId,
            MessageType = KendoMessageSerializer.GetMessageType(message),
            Payload = KendoMessageSerializer.Serialize(message),
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        busMock.Setup(b => b.Send(It.IsAny<UserCreatedEvent>(), It.IsAny<IDictionary<string, string>>()))
            .ThrowsAsync(new InvalidOperationException("ASB unavailable"));

        var relay = new OutboxRelayService(scopeFactory, CreateConfig(maxRetries: 2), NullLogger<OutboxRelayService>.Instance);

        await relay.ProcessBatchAsync(CancellationToken.None);

        // Message should have RetryCount incremented
        var failed = await db.OutboxMessages.FirstAsync(m => m.MessageId == message.MessageId);
        Assert.Equal(1, failed.RetryCount);
        Assert.Null(failed.ProcessedAt); // Not permanently skipped yet
        Assert.Contains("ASB unavailable", failed.LastError);
    }

    [Fact]
    public async Task ExecuteAsync_ExceedsMaxRetries_SkipsPermanently()
    {
        var (scopeFactory, db, busMock) = CreateRelaySut(nameof(ExecuteAsync_ExceedsMaxRetries_SkipsPermanently));

        var message = new UserCreatedEvent { UserId = Guid.NewGuid(), Email = "skip@example.com", DisplayName = "Skip Test" };
        db.OutboxMessages.Add(new OutboxMessage
        {
            MessageId = message.MessageId,
            MessageType = KendoMessageSerializer.GetMessageType(message),
            Payload = KendoMessageSerializer.Serialize(message),
            CreatedAt = DateTimeOffset.UtcNow,
            RetryCount = 4,
            LastError = "Previous failure"
        });
        await db.SaveChangesAsync();

        busMock.Setup(b => b.Send(It.IsAny<UserCreatedEvent>(), It.IsAny<IDictionary<string, string>>()))
            .ThrowsAsync(new InvalidOperationException("ASB unavailable"));

        var relay = new OutboxRelayService(scopeFactory, CreateConfig(maxRetries: 5), NullLogger<OutboxRelayService>.Instance);

        await relay.ProcessBatchAsync(CancellationToken.None);

        // Message should now be marked as processed (permanently skipped)
        var skipped = await db.OutboxMessages.FirstAsync(m => m.MessageId == message.MessageId);
        Assert.Equal(5, skipped.RetryCount);
        Assert.NotNull(skipped.ProcessedAt);
    }

    [Fact]
    public async Task ExecuteAsync_RespectsBatchSize()
    {
        var (scopeFactory, db, busMock) = CreateRelaySut(nameof(ExecuteAsync_RespectsBatchSize));

        // Seed 3 messages
        for (int i = 0; i < 3; i++)
        {
            var msg = new UserCreatedEvent { UserId = Guid.NewGuid(), Email = $"batch{i}@example.com", DisplayName = $"Batch {i}" };
            db.OutboxMessages.Add(new OutboxMessage
            {
                MessageId = msg.MessageId,
                MessageType = KendoMessageSerializer.GetMessageType(msg),
                Payload = KendoMessageSerializer.Serialize(msg),
                CreatedAt = DateTimeOffset.UtcNow.AddSeconds(i)
            });
        }
        await db.SaveChangesAsync();

        var relay = new OutboxRelayService(scopeFactory, CreateConfig(batchSize: 2), NullLogger<OutboxRelayService>.Instance);

        await relay.ProcessBatchAsync(CancellationToken.None);

        // Only 2 out of 3 should be published (batch size = 2)
        busMock.Verify(b => b.Send(It.IsAny<UserCreatedEvent>(), It.IsAny<IDictionary<string, string>>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ExecuteAsync_RespectsFifoOrder()
    {
        var (scopeFactory, db, busMock) = CreateRelaySut(nameof(ExecuteAsync_RespectsFifoOrder));

        // Seed messages with staggered creation times
        var emails = new[] { "first@example.com", "second@example.com", "third@example.com" };
        for (int i = 0; i < 3; i++)
        {
            var msg = new UserCreatedEvent { UserId = Guid.NewGuid(), Email = emails[i], DisplayName = $"Order {i}" };
            db.OutboxMessages.Add(new OutboxMessage
            {
                MessageId = msg.MessageId,
                MessageType = KendoMessageSerializer.GetMessageType(msg),
                Payload = KendoMessageSerializer.Serialize(msg),
                CreatedAt = DateTimeOffset.UtcNow.AddSeconds(i)
            });
        }
        await db.SaveChangesAsync();

        var relay = new OutboxRelayService(scopeFactory, CreateConfig(), NullLogger<OutboxRelayService>.Instance);

        await relay.ProcessBatchAsync(CancellationToken.None);

        // Verify FIFO: first message published first
        busMock.Verify(b => b.Send(It.Is<UserCreatedEvent>(e => e.Email == "first@example.com"), It.IsAny<IDictionary<string, string>>()), Times.Once);
        busMock.Verify(b => b.Send(It.Is<UserCreatedEvent>(e => e.Email == "second@example.com"), It.IsAny<IDictionary<string, string>>()), Times.Once);
        busMock.Verify(b => b.Send(It.Is<UserCreatedEvent>(e => e.Email == "third@example.com"), It.IsAny<IDictionary<string, string>>()), Times.Once);
    }
}
