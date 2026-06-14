using Kendo.Shared.Messaging;
using Kendo.Shared.Resilience;
using Kendo.UserService.Data;
using Kendo.UserService.Models;
using Kendo.Worker.Data;
using Kendo.Worker.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kendo.Tests.Worker;

[Trait("Category", "Unit")]
public class UserCreatedEventHandlerTests
{
    /// <summary>
    /// Pass-through resilience pipeline — executes the action directly without Polly.
    /// </summary>
    private class PassThroughResiliencePipeline : IResiliencePipeline
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct = default)
            => action(ct);
        public Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken ct = default)
            => action(ct);
    }

    /// <summary>
    /// Creates a pair of in-memory DbContexts (WorkerDbContext + UserService AppDbContext)
    /// that share the same database name (simulating same PostgreSQL instance).
    /// Returns the handler, plus the contexts for direct verification.
    /// </summary>
    private static (UserCreatedEventHandler handler, WorkerDbContext workerDb, AppDbContext userDb) CreateSut(string dbName)
    {
        var workerOptions = new DbContextOptionsBuilder<WorkerDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var workerDb = new WorkerDbContext(workerOptions);

        var userOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var userDb = new AppDbContext(userOptions);

        var resilientDb = new WorkerResilientDbContext(workerDb, new PassThroughResiliencePipeline());
        var logger = NullLogger<UserCreatedEventHandler>.Instance;

        var handler = new UserCreatedEventHandler(workerDb, resilientDb, userDb, logger);

        return (handler, workerDb, userDb);
    }

    /// <summary>
    /// Helper to seed a User in the database for handler tests.
    /// </summary>
    private static async Task<User> SeedUserAsync(AppDbContext db, Guid? userId = null, UserStatus status = UserStatus.Pending)
    {
        var user = new User
        {
            Id = userId ?? Guid.NewGuid(),
            Email = $"test{Guid.NewGuid():N}@example.com",
            DisplayName = "Test User",
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    /// <summary>
    /// Helper to seed an IdempotencyRecord for crash recovery / duplicate tests.
    /// </summary>
    private static async Task<IdempotencyRecord> SeedIdempotencyRecordAsync(
        WorkerDbContext db, Guid messageId, IdempotencyStatus status)
    {
        var record = new IdempotencyRecord
        {
            MessageId = messageId,
            HandlerName = "UserCreated",
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = status == IdempotencyStatus.Completed ? DateTimeOffset.UtcNow : null
        };
        db.IdempotencyRecords.Add(record);
        await db.SaveChangesAsync();
        return record;
    }

    // ── First-time processing ──────────────────────────────────────────

    [Fact]
    public async Task Handle_FirstTime_TransitionsUserToCompleted()
    {
        var (handler, workerDb, userDb) = CreateSut(nameof(Handle_FirstTime_TransitionsUserToCompleted));
        var user = await SeedUserAsync(userDb);
        var messageId = Guid.NewGuid();

        var message = new UserCreatedEvent
        {
            MessageId = messageId,
            UserId = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName
        };

        await handler.Handle(message);

        // Verify idempotency record
        var record = await workerDb.IdempotencyRecords.FirstAsync(r => r.MessageId == messageId);
        Assert.Equal(IdempotencyStatus.Completed, record.Status);
        Assert.Equal("UserCreated", record.HandlerName);
        Assert.NotNull(record.CompletedAt);

        // Verify user status
        var updatedUser = await userDb.Users.FirstAsync(u => u.Id == user.Id);
        Assert.Equal(UserStatus.Completed, updatedUser.Status);
        Assert.NotNull(updatedUser.ProcessedAt);
    }

    // ── Duplicate suppression ──────────────────────────────────────────

    [Fact]
    public async Task Handle_DuplicateCompletedMessage_SilentlyDiscards()
    {
        var (handler, workerDb, userDb) = CreateSut(nameof(Handle_DuplicateCompletedMessage_SilentlyDiscards));
        var user = await SeedUserAsync(userDb, status: UserStatus.Completed);
        var messageId = Guid.NewGuid();
        await SeedIdempotencyRecordAsync(workerDb, messageId, IdempotencyStatus.Completed);

        var message = new UserCreatedEvent
        {
            MessageId = messageId,
            UserId = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName
        };

        // Should not throw and should not modify user
        await handler.Handle(message);

        var record = await workerDb.IdempotencyRecords.FirstAsync(r => r.MessageId == messageId);
        Assert.Equal(IdempotencyStatus.Completed, record.Status);

        // User should still be Completed, ProcessedAt should remain null
        var updatedUser = await userDb.Users.FirstAsync(u => u.Id == user.Id);
        Assert.Equal(UserStatus.Completed, updatedUser.Status);
        Assert.Null(updatedUser.ProcessedAt);
    }

    // ── Crash recovery ─────────────────────────────────────────────────

    [Fact]
    public async Task Handle_DuplicateProcessingMessage_RecoversAndCompletes()
    {
        var (handler, workerDb, userDb) = CreateSut(nameof(Handle_DuplicateProcessingMessage_RecoversAndCompletes));
        var user = await SeedUserAsync(userDb);
        var messageId = Guid.NewGuid();
        await SeedIdempotencyRecordAsync(workerDb, messageId, IdempotencyStatus.Processing);

        var message = new UserCreatedEvent
        {
            MessageId = messageId,
            UserId = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName
        };

        await handler.Handle(message);

        // Verify idempotency record was completed
        var record = await workerDb.IdempotencyRecords.FirstAsync(r => r.MessageId == messageId);
        Assert.Equal(IdempotencyStatus.Completed, record.Status);
        Assert.NotNull(record.CompletedAt);

        // Verify user was updated
        var updatedUser = await userDb.Users.FirstAsync(u => u.Id == user.Id);
        Assert.Equal(UserStatus.Completed, updatedUser.Status);
        Assert.NotNull(updatedUser.ProcessedAt);
    }

    // ── Unknown user ───────────────────────────────────────────────────

    [Fact]
    public async Task Handle_UnknownUserId_RecordsFailed()
    {
        var (handler, workerDb, userDb) = CreateSut(nameof(Handle_UnknownUserId_RecordsFailed));
        var messageId = Guid.NewGuid();
        var unknownUserId = Guid.NewGuid();

        var message = new UserCreatedEvent
        {
            MessageId = messageId,
            UserId = unknownUserId,
            Email = "unknown@example.com",
            DisplayName = "Unknown"
        };

        await handler.Handle(message);

        // Verify idempotency record is Failed
        var record = await workerDb.IdempotencyRecords.FirstAsync(r => r.MessageId == messageId);
        Assert.Equal(IdempotencyStatus.Failed, record.Status);
        Assert.NotNull(record.CompletedAt);
    }

    // ── Unique constraint enforcement ──────────────────────────────────

    [Fact]
    public async Task Handle_InsertDuplicateIdempotencyKey_DoesNotThrow()
    {
        var (handler, _, userDb) = CreateSut(nameof(Handle_InsertDuplicateIdempotencyKey_DoesNotThrow));
        var user = await SeedUserAsync(userDb);
        var messageId = Guid.NewGuid();

        // First call — should succeed
        var message1 = new UserCreatedEvent
        {
            MessageId = messageId,
            UserId = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName
        };
        await handler.Handle(message1);

        // Second call with same UserId but different MessageId — different idempotency key, should process
        var user2 = await SeedUserAsync(userDb, status: UserStatus.Pending);
        var message2 = new UserCreatedEvent
        {
            MessageId = Guid.NewGuid(),
            UserId = user2.Id,
            Email = user2.Email,
            DisplayName = user2.DisplayName
        };

        var ex = await Record.ExceptionAsync(() => handler.Handle(message2));

        Assert.Null(ex);

        // Both users should be completed
        var u1 = await userDb.Users.FirstAsync(u => u.Id == user.Id);
        Assert.Equal(UserStatus.Completed, u1.Status);
        var u2 = await userDb.Users.FirstAsync(u => u.Id == user2.Id);
        Assert.Equal(UserStatus.Completed, u2.Status);
    }

    // ── Failed message retry ───────────────────────────────────────────

    [Fact]
    public async Task Handle_FailedMessage_RetriesAndCompletes()
    {
        var (handler, workerDb, userDb) = CreateSut(nameof(Handle_FailedMessage_RetriesAndCompletes));
        var user = await SeedUserAsync(userDb);
        var messageId = Guid.NewGuid();
        await SeedIdempotencyRecordAsync(workerDb, messageId, IdempotencyStatus.Failed);

        var message = new UserCreatedEvent
        {
            MessageId = messageId,
            UserId = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName
        };

        await handler.Handle(message);

        // Verify retry completed the message
        var record = await workerDb.IdempotencyRecords.FirstAsync(r => r.MessageId == messageId);
        Assert.Equal(IdempotencyStatus.Completed, record.Status);

        var updatedUser = await userDb.Users.FirstAsync(u => u.Id == user.Id);
        Assert.Equal(UserStatus.Completed, updatedUser.Status);
    }
}
