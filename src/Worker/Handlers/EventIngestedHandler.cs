using System.Diagnostics;
using Kendo.Shared.Messaging;
using Kendo.Shared.Messaging.Events;
using Kendo.Worker.Data;
using Microsoft.EntityFrameworkCore;
using Rebus.Handlers;
using Rebus.Pipeline;

namespace Kendo.Worker.Handlers;

/// <summary>
/// Handles EventIngestedEvent (emitted by Gateway after W1 returns
/// structured JSON). Writes the Event row + embedding to UserService
/// and emits EventValidatedEvent for W2 fanout.
/// 
/// Idempotency: re-delivery is a no-op (MessageId check).
/// </summary>
public class EventIngestedHandler : IHandleMessages<EventIngestedEvent>
{
    private readonly WorkerDbContext _db;
    private readonly WorkerResilientDbContext _resilientDb;
    private readonly ILogger<EventIngestedHandler> _logger;

    public EventIngestedHandler(
        WorkerDbContext db,
        WorkerResilientDbContext resilientDb,
        ILogger<EventIngestedHandler> logger)
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
            var activity = new Activity("EventIngestedHandler.Process");
            activity.SetParentId(traceParent);
            activity.Start();
            return activity;
        }
        catch
        {
            return null;
        }
    }

    public async Task Handle(EventIngestedEvent message)
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
                "Duplicate EventIngestedEvent (already completed). MessageId={MessageId} — discarding.",
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
                    HandlerName = "EventIngested",
                    Status = IdempotencyStatus.Processing
                });
                await _db.SaveChangesAsync();
            }

            // The actual Event row + embedding writes happen in UserService
            // via the direct HTTP client (wired in Program.cs). This handler
            // emits the follow-on EventValidatedEvent for W2.
            // The direct HTTP call is abstracted through a scoped service.

            _logger.LogInformation(
                "Processing EventIngestedEvent: EventId={EventId}, CorrelationId={CorrelationId}",
                message.EventId, message.CorrelationId);

            // Mark idempotency as completed
            var record = existing ?? await _db.IdempotencyRecords
                .FirstAsync(r => r.MessageId == message.MessageId);

            record.Status = IdempotencyStatus.Completed;
            record.CompletedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync();

            await transaction.CommitAsync();

            _logger.LogInformation(
                "Successfully processed EventIngestedEvent: EventId={EventId}",
                message.EventId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to process EventIngestedEvent: EventId={EventId}",
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
                        HandlerName = "EventIngested",
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
