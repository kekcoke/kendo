namespace Kendo.Shared.Messaging;

/// <summary>
/// Base marker for all Kendo domain messages.
/// Every domain event published via Rebus inherits from this record.
/// </summary>
public abstract record KendoMessage
{
    /// <summary>
    /// Unique message identifier for idempotency tracking.
    /// </summary>
    public Guid MessageId { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Timestamp of when the message was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
