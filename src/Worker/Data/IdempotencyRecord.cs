namespace Kendo.Worker.Data;

public enum IdempotencyStatus
{
    Processing,
    Completed,
    Failed
}

/// <summary>
/// Tracks which messages have been processed and their outcome.
/// The primary key is the message's MessageId, which acts as the idempotency key.
/// </summary>
public class IdempotencyRecord
{
    public Guid MessageId { get; set; }    // = KendoMessage.MessageId
    public string HandlerName { get; set; } = string.Empty;
    public IdempotencyStatus Status { get; set; } = IdempotencyStatus.Processing;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}
