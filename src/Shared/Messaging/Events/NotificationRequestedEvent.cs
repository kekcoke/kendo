namespace Kendo.Shared.Messaging.Events;

/// <summary>
/// Published by the Worker after any of: EventIngestedHandler (welcome
/// notification), EventValidatedHandler (validation-result notification), or
/// UserCreatedEventHandler (onboarding notification). Consumed by the Worker
/// (NotificationRequestedHandler — see day_19_spec.md), which calls FastAPI for
/// summarization and dispatches the notification.
/// </summary>
public sealed record NotificationRequestedEvent : KendoMessage
{
    /// <summary>Recipient user ID.</summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Template identifier: e.g. "event.welcome" | "event.conflict" | "user.onboarding".
    /// </summary>
    public string TemplateId { get; init; } = "";

    /// <summary>
    /// Notification tone: "neutral" | "warm" | "formal".
    /// </summary>
    public string Tone { get; init; } = "neutral";

    /// <summary>Related entity ID (EventId / UserId depending on template).</summary>
    public Guid? RelatedEntityId { get; init; }

    /// <summary>Related entity type: "event" | "user".</summary>
    public string? RelatedEntityType { get; init; }

    /// <summary>When the notification was requested.</summary>
    public DateTimeOffset RequestedAt { get; init; }
}
