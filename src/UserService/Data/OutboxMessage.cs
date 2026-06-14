namespace Kendo.UserService.Data;

/// <summary>
/// Represents a domain event awaiting publication via the transactional outbox pattern.
/// Written atomically with the business transaction; processed asynchronously by
/// <c>OutboxRelayService</c>.
/// </summary>
public class OutboxMessage
{
    public Guid Id { get; set; }
    public Guid MessageId { get; set; }
    public string MessageType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; set; }
    public int RetryCount { get; set; }
    public string? LastError { get; set; }

    /// <summary>
    /// W3C TraceContext traceparent value captured at write time.
    /// Enables end-to-end trace correlation between the HTTP request Activity
    /// and the consumer-side span in the Worker.
    /// </summary>
    public string? TraceContext { get; set; }
}
