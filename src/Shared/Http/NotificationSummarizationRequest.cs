namespace Kendo.Shared.Http;

/// <summary>
/// Request DTO for the FastAPI W7 notification summarization endpoint.
/// Sent as JSON body to POST /v1/notifications/summarize.
/// </summary>
public class NotificationSummarizationRequest
{
    /// <summary>ID of the event to summarize.</summary>
    public Guid EventId { get; init; }

    /// <summary>ID of the user receiving the notification.</summary>
    public Guid UserId { get; init; }

    /// <summary>Optional template ID for context.</summary>
    public string TemplateId { get; init; } = "";

    /// <summary>Tone profile: "professional", "friendly", or "urgent".</summary>
    public string Tone { get; init; } = "friendly";
}
