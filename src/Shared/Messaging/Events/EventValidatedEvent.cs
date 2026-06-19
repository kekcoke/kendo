namespace Kendo.Shared.Messaging.Events;

/// <summary>
/// Published by the Worker after EventIngestedHandler writes the Event row AND
/// W2 returns a validation result. Consumed by the Worker (EventValidatedHandler —
/// see day_19_spec.md), which persists the EventValidation row and may emit a
/// NotificationRequestedEvent for W7.
/// </summary>
public sealed record EventValidatedEvent : KendoMessage
{
    /// <summary>The event that was validated.</summary>
    public Guid EventId { get; init; }

    /// <summary>Ties to the originating request span.</summary>
    public Guid CorrelationId { get; init; }

    /// <summary>Whether the event passed validation.</summary>
    public bool Ok { get; init; }

    /// <summary>Serialized list of conflict objects.</summary>
    public string ConflictsJson { get; init; } = "[]";

    /// <summary>Serialized list of suggestions.</summary>
    public string SuggestionsJson { get; init; } = "[]";

    /// <summary>Optional agent reasoning trace for auditability.</summary>
    public string? ReasoningTrace { get; init; }

    /// <summary>When the validation occurred.</summary>
    public DateTimeOffset ValidatedAt { get; init; }
}
