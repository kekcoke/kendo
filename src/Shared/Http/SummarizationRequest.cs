namespace Kendo.Shared.Http;

/// <summary>
/// Request DTO for the FastAPI W7 notification summarization endpoint.
/// </summary>
public class SummarizationRequest
{
    public Guid? EventId { get; init; }
    public Guid UserId { get; init; }
    public string TemplateId { get; init; } = "";
    public string Tone { get; init; } = "neutral";
}
