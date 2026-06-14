using Kendo.Shared.Messaging;
using Kendo.UserService.Data;
using Kendo.UserService.Models;
using Kendo.Worker.Data;
using Microsoft.EntityFrameworkCore;
using Rebus.Handlers;

namespace Kendo.Worker.Handlers;

/// <summary>
/// Handles UserCreatedEvent messages with idempotency enforcement.
/// 
/// Idempotency strategy:
/// - Uses the KendoMessage.MessageId as the idempotency key
/// - Attempts INSERT of IdempotencyRecord (MessageId PK) → unique constraint catches duplicates
/// - Completed → silent discard. Processing → crash recovery (re-process).
/// - First-time insert → process and update to Completed.
/// 
/// All DB operations (idempotency + user update) occur within a single transaction
/// via WorkerDbContext.Database.BeginTransaction.
/// </summary>
public class UserCreatedEventHandler : IHandleMessages<UserCreatedEvent>
{
    private readonly WorkerDbContext _db;
    private readonly WorkerResilientDbContext _resilientDb;
    private readonly AppDbContext _userDb;
    private readonly ILogger<UserCreatedEventHandler> _logger;

    public UserCreatedEventHandler(
        WorkerDbContext db,
        WorkerResilientDbContext resilientDb,
        AppDbContext userDb,
        ILogger<UserCreatedEventHandler> logger)
    {
        _db = db;
        _resilientDb = resilientDb;
        _userDb = userDb;
        _logger = logger;
    }

    public async Task Handle(UserCreatedEvent message)
    {
        var handlerName = "UserCreated";
        _logger.LogInformation(
            "Processing UserCreatedEvent: MessageId={MessageId}, UserId={UserId}",
            message.MessageId, message.UserId);

        // Step 1: Try idempotency insert — unique constraint detects duplicates
        var existingRecord = await _resilientDb.ExecuteAsync(async ct =>
            await _db.IdempotencyRecords.FirstOrDefaultAsync(r => r.MessageId == message.MessageId, ct));

        if (existingRecord is not null)
        {
            switch (existingRecord.Status)
            {
                case IdempotencyStatus.Completed:
                    _logger.LogInformation(
                        "Duplicate message detected (already completed). MessageId={MessageId}, UserId={UserId} — discarding.",
                        message.MessageId, message.UserId);
                    return;

                case IdempotencyStatus.Processing:
                    _logger.LogWarning(
                        "Crash recovery: MessageId={MessageId}, UserId={UserId} was left in Processing state. Re-processing.",
                        message.MessageId, message.UserId);
                    // Fall through to re-process
                    break;

                case IdempotencyStatus.Failed:
                    _logger.LogWarning(
                        "Retrying previously failed message. MessageId={MessageId}, UserId={UserId}.",
                        message.MessageId, message.UserId);
                    // Fall through to re-process
                    break;

                default:
                    _logger.LogWarning(
                        "Unknown idempotency status {Status} for MessageId={MessageId}. Re-processing.",
                        existingRecord.Status, message.MessageId);
                    break;
            }
        }

        // Step 2: Begin transaction
        await using var transaction = await _db.Database.BeginTransactionAsync();

        try
        {
            // Step 3: Insert idempotency record (first time) or leave existing (crash recovery)
            if (existingRecord is null)
            {
                _db.IdempotencyRecords.Add(new IdempotencyRecord
                {
                    MessageId = message.MessageId,
                    HandlerName = handlerName,
                    Status = IdempotencyStatus.Processing
                });
                await _db.SaveChangesAsync();
            }

            // Step 4: Lookup user
            var user = await _userDb.Users.FirstOrDefaultAsync(u => u.Id == message.UserId);

            if (user is null)
            {
                _logger.LogWarning(
                    "User not found for UserCreatedEvent: MessageId={MessageId}, UserId={UserId}. Recording as Failed.",
                    message.MessageId, message.UserId);

                var record = existingRecord ?? await _db.IdempotencyRecords
                    .FirstAsync(r => r.MessageId == message.MessageId);

                record.Status = IdempotencyStatus.Failed;
                record.CompletedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync();

                await transaction.CommitAsync();
                return;
            }

            // Step 5: Update user status
            if (user.Status == UserStatus.Pending)
            {
                user.Status = UserStatus.Processing;
            }

            // Step 6: Do work
            _logger.LogInformation(
                "Processing user {UserId} ({Email}). Current status: {Status}",
                user.Id, user.Email, user.Status);

            // Simulated async processing — for M2.3 this completes immediately.
            // Future milestones will add actual logic here.

            // Step 7: Mark user as completed
            user.Status = UserStatus.Completed;
            user.ProcessedAt = DateTimeOffset.UtcNow;
            await _userDb.SaveChangesAsync();

            // Step 8: Mark idempotency as completed
            var completionRecord = existingRecord ?? await _db.IdempotencyRecords
                .FirstAsync(r => r.MessageId == message.MessageId);

            completionRecord.Status = IdempotencyStatus.Completed;
            completionRecord.CompletedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync();

            await transaction.CommitAsync();

            _logger.LogInformation(
                "Successfully processed UserCreatedEvent: MessageId={MessageId}, UserId={UserId}.",
                message.MessageId, message.UserId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to process UserCreatedEvent: MessageId={MessageId}, UserId={UserId}. Rolling back.",
                message.MessageId, message.UserId);

            try
            {
                await transaction.RollbackAsync();
            }
            catch (Exception rollbackEx)
            {
                _logger.LogWarning(rollbackEx,
                    "Transaction rollback failed for MessageId={MessageId}.", message.MessageId);
            }

            // Record the failure in the idempotency table independently (no transaction)
            if (existingRecord is null)
            {
                // First attempt failed — insert a failed record
                // If this also fails (e.g. DB down), Rebus will retry
                try
                {
                    _db.IdempotencyRecords.Add(new IdempotencyRecord
                    {
                        MessageId = message.MessageId,
                        HandlerName = handlerName,
                        Status = IdempotencyStatus.Failed,
                        CompletedAt = DateTimeOffset.UtcNow
                    });
                    await _db.SaveChangesAsync();
                }
                catch (Exception fallbackEx)
                {
                    _logger.LogWarning(fallbackEx,
                        "Failed to record idempotency failure for MessageId={MessageId}.",
                        message.MessageId);
                }
            }
            else
            {
                // Update existing record to Failed
                try
                {
                    existingRecord.Status = IdempotencyStatus.Failed;
                    existingRecord.CompletedAt = DateTimeOffset.UtcNow;
                    await _db.SaveChangesAsync();
                }
                catch (Exception fallbackEx)
                {
                    _logger.LogWarning(fallbackEx,
                        "Failed to update idempotency record to Failed for MessageId={MessageId}.",
                        message.MessageId);
                }
            }

            // Re-throw so Rebus knows to retry / DLQ
            throw;
        }
    }
}
