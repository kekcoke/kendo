namespace Kendo.Shared.Messaging;

/// <summary>
/// Wraps metadata about a message that was moved to the Dead Letter Queue.
/// Rebus delivers the original message body to the DLQ handler;
/// this type serves as a typed marker and carries the relevant headers.
/// </summary>
public sealed record DeadLetteredMessage : KendoMessage
{
    /// <summary>
    /// The original message ID from the failed message.
    /// </summary>
    public Guid OriginalMessageId { get; init; }

    /// <summary>
    /// The original message type (e.g. "UserCreatedEvent").
    /// </summary>
    public string OriginalMessageType { get; init; } = string.Empty;

    /// <summary>
    /// Reason for dead-lettering (from ASB headers).
    /// </summary>
    public string? DeadLetterReason { get; init; }

    /// <summary>
    /// Error description from ASB headers.
    /// </summary>
    public string? DeadLetterErrorDescription { get; init; }

    /// <summary>
    /// How many times delivery was attempted before dead-lettering.
    /// </summary>
    public int DeliveryCount { get; init; }

    /// <summary>
    /// When the original message was enqueued (from headers).
    /// </summary>
    public DateTimeOffset? EnqueuedTime { get; init; }
}
