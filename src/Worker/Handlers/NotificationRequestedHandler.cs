using System.Diagnostics;
using System.Text;
using Kendo.Shared.Http;
using Kendo.Shared.Messaging;
using Kendo.Shared.Messaging.Events;
using Kendo.Worker.Data;
using Kendo.Worker.Models;
using Kendo.Worker.Workers;
using Microsoft.EntityFrameworkCore;
using Rebus.Handlers;
using Rebus.Pipeline;

namespace Kendo.Worker.Handlers;

/// <summary>
/// Handles NotificationRequestedEvent. Calls FastAPI W7 summarization
/// endpoint via SSE, collects the rendered body, and enqueues it to
/// the NotificationDispatcherHostedService for delivery.
/// 
/// Idempotency: re-delivery is a no-op (MessageId check).
/// </summary>
public class NotificationRequestedHandler : IHandleMessages<NotificationRequestedEvent>
{
    private readonly WorkerDbContext _db;
    private readonly WorkerResilientDbContext _resilientDb;
    private readonly INotificationSummarizationClient _summarizationClient;
    private readonly NotificationDispatcherChannel _dispatcherChannel;
    private readonly ILogger<NotificationRequestedHandler> _logger;

    public NotificationRequestedHandler(
        WorkerDbContext db,
        WorkerResilientDbContext resilientDb,
        INotificationSummarizationClient summarizationClient,
        NotificationDispatcherChannel dispatcherChannel,
        ILogger<NotificationRequestedHandler> logger)
    {
        _db = db;
        _resilientDb = resilientDb;
        _summarizationClient = summarizationClient;
        _dispatcherChannel = dispatcherChannel;
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
            var activity = new Activity("NotificationRequestedHandler.Process");
            activity.SetParentId(traceParent);
            activity.Start();
            return activity;
        }
        catch
        {
            return null;
        }
    }

    public async Task Handle(NotificationRequestedEvent message)
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
                "Duplicate NotificationRequestedEvent (already completed). MessageId={MessageId} — discarding.",
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
                    HandlerName = "NotificationRequested",
                    Status = IdempotencyStatus.Processing
                });
                await _db.SaveChangesAsync();
            }

            _logger.LogInformation(
                "Processing NotificationRequestedEvent: UserId={UserId}, TemplateId={TemplateId}",
                message.UserId, message.TemplateId);

            // Call FastAPI W7 via SSE summarization and collect the rendered body
            var renderedBody = new StringBuilder();
            string? promptVersion = null;
            string? traceId = null;

            try
            {
                await foreach (var chunk in _summarizationClient.SummarizeStreamAsync(
                    new NotificationSummarizationRequest
                    {
                        EventId = message.RelatedEntityType == "event" ? message.RelatedEntityId : Guid.Empty,
                        UserId = message.UserId,
                        TemplateId = message.TemplateId,
                        Tone = message.Tone
                    },
                    CancellationToken.None))
                {
                    switch (chunk.Event)
                    {
                        case "chunk":
                            if (chunk.Text != null)
                                renderedBody.Append(chunk.Text);
                            break;

                        case "done":
                            promptVersion = chunk.PromptVersion ?? "w7-notification-v1";
                            traceId = chunk.TraceId;
                            _logger.LogInformation(
                                "Notification summarization complete: traceId={TraceId}, promptVersion={PromptVersion}, totalTokens={TotalTokens}",
                                traceId, promptVersion, chunk.TotalTokens);
                            break;

                        case "error":
                            _logger.LogError(
                                "FastAPI summarization error: title={Title}, status={Status}, detail={Detail}",
                                chunk.Title, chunk.Status, chunk.Detail);
                            throw new InvalidOperationException(
                                $"FastAPI summarization failed: {chunk.Title} — {chunk.Detail}");
                    }
                }
            }
            catch (NotificationSummarizationClientException ex)
            {
                _logger.LogError(ex,
                    "NotificationSummarizationClient rejected summarization for UserId={UserId}: {Title} (HTTP {Status})",
                    message.UserId, ex.ErrorTitle, ex.StatusCode);
                throw; // Rebus will retry and eventually DLQ
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                // On SSE stream error mid-generation: discard partial body,
                // fall back to template-based notification, log warning, do NOT DLQ
                _logger.LogWarning(ex,
                    "SSE stream error mid-generation for UserId={UserId}. Falling back to template-based notification.",
                    message.UserId);
                renderedBody.Clear();
                renderedBody.Append($"[Template: {message.TemplateId}] Event notification for {message.UserId}.");
                promptVersion = "template-fallback";
            }

            // Enqueue the rendered notification for delivery
            await _dispatcherChannel.Writer.WriteAsync(new NotificationDispatchJob
            {
                UserId = message.UserId,
                TemplateId = message.TemplateId,
                RenderedBody = renderedBody.ToString(),
                PromptVersion = promptVersion ?? "unknown",
                RequestedAt = message.RequestedAt
            });

            _logger.LogInformation(
                "Notification rendered and enqueued for UserId={UserId}, TemplateId={TemplateId}.",
                message.UserId, message.TemplateId);

            // Mark idempotency as completed
            var record = existing ?? await _db.IdempotencyRecords
                .FirstAsync(r => r.MessageId == message.MessageId);

            record.Status = IdempotencyStatus.Completed;
            record.CompletedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync();

            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to process NotificationRequestedEvent: UserId={UserId}",
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
                        HandlerName = "NotificationRequested",
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
