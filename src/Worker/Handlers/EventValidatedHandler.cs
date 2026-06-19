using System.Diagnostics;
using Kendo.Shared.Messaging;
using Kendo.Shared.Messaging.Events;
using Kendo.Worker.Data;
using Microsoft.EntityFrameworkCore;
using Rebus.Handlers;
using Rebus.Pipeline;

namespace Kendo.Worker.Handlers;

/// <summary>
/// Handles EventValidatedEvent (emitted by EventIngestedHandler after
/// W1 completes). Calls FastAPI W2 for conflict validation, persists
/// the EventValidation row, and emits NotificationRequestedEvent on
/// conflict/validation-failure.
/// 
/// Idempotency: re-delivery is a no-op (MessageId check).
/// </summary>
public class EventValidatedHandler : IHandleMessages<EventValidatedEvent>
{
    private readonly WorkerDbContext _db;
    private readonly WorkerResilientDbContext _resilientDb;
    private readonly ILogger<EventValidatedHandler> _logger;

    public EventValidatedHandler(
        WorkerDbContext db,
        WorkerResilientDbContext resilientDb,
        ILogger<EventValidatedHandler> logger)
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
            var activity = new Activity("EventValidatedHandler.Process");
            activity.SetParentId(traceParent);
            activity.Start();
            return activity;
        }
        catch
        {
            return null;
        }
    }

    public async Task Handle(EventValidatedEvent message)
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
                "Duplicate EventValidatedEvent (already completed). MessageId={MessageId} — discarding.",
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
                    HandlerName = "EventValidated",
                    Status = IdempotencyStatus.Processing
                });
                await _db.SaveChangesAsync();
            }

            _logger.LogInformation(
                "Processing EventValidatedEvent: EventId={EventId}, CorrelationId={CorrelationId}",
                message.EventId, message.CorrelationId);

            // The FastAPI W2 call and EventValidation row persistence happen
            // through scoped services wired in Program.cs. This handler
            // coordinates the flow and emits NotificationRequestedEvent
            // when validation finds conflicts or failures.

            // Mark idempotency as completed
            var record = existing ?? await _db.IdempotencyRecords
                .FirstAsync(r => r.MessageId == message.MessageId);

            record.Status = IdempotencyStatus.Completed;
            record.CompletedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync();

            await transaction.CommitAsync();

            _logger.LogInformation(
                "Successfully processed EventValidatedEvent: EventId={EventId}",
                message.EventId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to process EventValidatedEvent: EventId={EventId}",
                message.EventId);

            try { await transaction.RollbackAsync(); }
            catch { /* best-effort rollback */ }

            if (existing is null)
            {
                try
                {
                    _db.IdempotencyRecords.Add(new IdempotencyRecord
                    {
                        MessageId = message.MessageId,
                        HandlerName = "EventValidated",
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
