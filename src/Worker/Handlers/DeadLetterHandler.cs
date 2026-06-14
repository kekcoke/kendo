using Kendo.Shared.Messaging;
using Kendo.Worker.Data;
using Microsoft.EntityFrameworkCore;
using Rebus.Handlers;

namespace Kendo.Worker.Handlers;

/// <summary>
/// Handles messages that have been moved to the Azure Service Bus Dead Letter Queue.
/// 
/// This handler:
/// - Logs every DLQ message with original headers, routing info, and error details
/// - Persists a DlqRecord for audit trail and alerting
/// - Acknowledges the message on completion to prevent re-delivery loops on the DLQ
/// 
/// If persistence fails, the handler logs at LogLevel.Critical and still acknowledges
/// the message (fail-safe — log-based recovery). The DLQ metadata is preserved in
/// structured logs so manual recovery can re-queue from there.
/// </summary>
public class DeadLetterHandler : IHandleMessages<DeadLetteredMessage>
{
    private readonly WorkerDbContext _db;
    private readonly ILogger<DeadLetterHandler> _logger;

    public DeadLetterHandler(
        WorkerDbContext db,
        ILogger<DeadLetterHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Handle(DeadLetteredMessage message)
    {
        _logger.LogError(
            "DLQ message received: OriginalMessageId={OriginalMessageId}, " +
            "OriginalMessageType={OriginalMessageType}, " +
            "DeadLetterReason={DeadLetterReason}, " +
            "DeadLetterErrorDescription={DeadLetterErrorDescription}, " +
            "DeliveryCount={DeliveryCount}, " +
            "EnqueuedTime={EnqueuedTime}",
            message.OriginalMessageId,
            message.OriginalMessageType,
            message.DeadLetterReason ?? "(none)",
            message.DeadLetterErrorDescription ?? "(none)",
            message.DeliveryCount,
            message.EnqueuedTime?.ToString("O") ?? "(unknown)");

        try
        {
            // Persist DLQ record for audit trail
            _db.DlqRecords.Add(new DlqRecord
            {
                Id = Guid.NewGuid(),
                OriginalMessageId = message.OriginalMessageId,
                OriginalMessageType = message.OriginalMessageType,
                DeadLetterReason = message.DeadLetterReason,
                DeadLetterErrorDescription = message.DeadLetterErrorDescription,
                DeliveryCount = message.DeliveryCount,
                EnqueuedTime = message.EnqueuedTime,
                DetectedAt = DateTimeOffset.UtcNow,
                Alerted = false
            });

            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogCritical(
                ex,
                "Failed to persist DlqRecord for OriginalMessageId={OriginalMessageId}. " +
                "DLQ metadata has been logged above. " +
                "OriginalMessageType={OriginalMessageType}, " +
                "DeadLetterReason={DeadLetterReason}",
                message.OriginalMessageId,
                message.OriginalMessageType,
                message.DeadLetterReason ?? "(none)");

            // Fail-safe: acknowledge the message anyway to prevent DLQ re-delivery loops
        }

        _logger.LogInformation(
            "DLQ message acknowledged: OriginalMessageId={OriginalMessageId}",
            message.OriginalMessageId);
    }
}
