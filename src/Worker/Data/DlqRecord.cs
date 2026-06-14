namespace Kendo.Worker.Data;

/// <summary>
/// Represents a message that was moved to the Azure Service Bus Dead Letter Queue.
/// Persisted for audit trail and alerting reference.
/// </summary>
public class DlqRecord
{
    public Guid Id { get; set; }
    public Guid OriginalMessageId { get; set; }
    public string OriginalMessageType { get; set; } = string.Empty;
    public string? DeadLetterReason { get; set; }
    public string? DeadLetterErrorDescription { get; set; }
    public int DeliveryCount { get; set; }
    public DateTimeOffset? EnqueuedTime { get; set; }
    public DateTimeOffset DetectedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool Alerted { get; set; }
}
