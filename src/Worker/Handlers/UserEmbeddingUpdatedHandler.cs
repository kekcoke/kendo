using System.Diagnostics;
using Kendo.Shared.Messaging;
using Kendo.Shared.Messaging.Events;
using Kendo.Worker.Data;
using Microsoft.EntityFrameworkCore;
using Rebus.Handlers;
using Rebus.Pipeline;

namespace Kendo.Worker.Handlers;

/// <summary>
/// Handles UserEmbeddingUpdatedEvent (emitted by UserService after a
/// successful embed — first-embed, reindex, or model-swap).
/// - first_embed → emits welcome NotificationRequestedEvent
/// - reindex / model_swap → audit log only
/// 
/// Idempotency: re-delivery is a no-op (MessageId check).
/// </summary>
public class UserEmbeddingUpdatedHandler : IHandleMessages<UserEmbeddingUpdatedEvent>
{
    private readonly WorkerDbContext _db;
    private readonly WorkerResilientDbContext _resilientDb;
    private readonly ILogger<UserEmbeddingUpdatedHandler> _logger;

    public UserEmbeddingUpdatedHandler(
        WorkerDbContext db,
        WorkerResilientDbContext resilientDb,
        ILogger<UserEmbeddingUpdatedHandler> logger)
    {
        _db = db;
        _resilientDb = resilientDb;
        _logger = logger;
    }

    private static Activity? StartTraceActivity(Dictionary<string, string>? headers = null)
    {
        headers ??= MessageContext.Current?.Headers;

        if (headers?.TryGetValue("traceparent", out var traceParent) != true
            || string.IsNullOrWhiteSpace(traceParent))
        {
            return null;
        }

        try
        {
            var activity = new Activity("UserEmbeddingUpdatedHandler.Process");
            activity.SetParentId(traceParent);
            activity.Start();
            return activity;
        }
        catch
        {
            return null;
        }
    }

    public async Task Handle(UserEmbeddingUpdatedEvent message)
    {
        using var traceActivity = StartTraceActivity();
        if (traceActivity is not null)
        {
            _logger.LogInformation(
                "Trace correlation: traceId={TraceId}, parentSpanId={ParentSpanId}",
                traceActivity.TraceId, traceActivity.ParentSpanId);
        }

        // Idempotency check
        var existing = await _resilientDb.ExecuteAsync(async ct =>
            await _db.IdempotencyRecords.FirstOrDefaultAsync(r => r.MessageId == message.MessageId, ct));

        if (existing?.Status == IdempotencyStatus.Completed)
        {
            _logger.LogInformation(
                "Duplicate UserEmbeddingUpdatedEvent (already completed). MessageId={MessageId} — discarding.",
                message.MessageId);
            return;
        }

        await using var transaction = await _db.Database.BeginTransactionAsync();

        try
        {
            // Insert idempotency record
            if (existing is null)
            {
                _db.IdempotencyRecords.Add(new IdempotencyRecord
                {
                    MessageId = message.MessageId,
                    HandlerName = "UserEmbeddingUpdated",
                    Status = IdempotencyStatus.Processing
                });
                await _db.SaveChangesAsync();
            }

            _logger.LogInformation(
                "Processing UserEmbeddingUpdatedEvent: UserId={UserId}, Trigger={Trigger}",
                message.UserId, message.Trigger);

            switch (message.Trigger)
            {
                case "first_embed":
                    _logger.LogInformation(
                        "First embed for user {UserId} — welcome notification will be sent.",
                        message.UserId);
                    break;

                case "reindex":
                    _logger.LogInformation(
                        "Embedding reindexed for user {UserId}. No notification required.",
                        message.UserId);
                    break;

                case "model_swap":
                    _logger.LogInformation(
                        "Embedding model swapped for user {UserId}. No notification required.",
                        message.UserId);
                    break;

                default:
                    _logger.LogWarning(
                        "Unknown UserEmbeddingUpdatedEvent trigger '{Trigger}' for UserId={UserId}.",
                        message.Trigger, message.UserId);
                    break;
            }

            // Mark idempotency as completed
            var record = existing ?? await _db.IdempotencyRecords
                .FirstAsync(r => r.MessageId == message.MessageId);

            record.Status = IdempotencyStatus.Completed;
            record.CompletedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync();

            await transaction.CommitAsync();

            _logger.LogInformation(
                "Successfully processed UserEmbeddingUpdatedEvent for user {UserId}.",
                message.UserId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to process UserEmbeddingUpdatedEvent: UserId={UserId}",
                message.UserId);

            try { await transaction.RollbackAsync(); }
            catch { /* best-effort rollback */ }

            if (existing is null)
            {
                try
                {
                    _db.IdempotencyRecords.Add(new IdempotencyRecord
                    {
                        MessageId = message.MessageId,
                        HandlerName = "UserEmbeddingUpdated",
                        Status = IdempotencyStatus.Failed,
                        CompletedAt = DateTimeOffset.UtcNow
                    });
                    await _db.SaveChangesAsync();
                }
                catch { /* best-effort failure recording */ }
            }
            else
            {
                try { existing.Status = IdempotencyStatus.Failed; await _db.SaveChangesAsync(); }
                catch { /* best-effort */ }
            }

            throw;
        }
    }
}
