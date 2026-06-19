namespace Kendo.Shared.Http;

/// <summary>
/// Configuration options for the Worker's FastAPI summarization client.
/// Bound from Kendo:FastApi:Summarization configuration section.
/// Separate from FastApiOptions to allow independent timeout/breaker tuning.
/// </summary>
public class FastApiSummarizationOptions
{
    public string BaseUrl { get; set; } = "http://fastapi:8000";
    public int TimeoutSeconds { get; set; } = 15;
    public int CircuitBreakerFailures { get; set; } = 3;
    public int CircuitBreakerBreakSeconds { get; set; } = 30;
}
