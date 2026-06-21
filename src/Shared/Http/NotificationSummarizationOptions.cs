namespace Kendo.Shared.Http;

/// <summary>
/// Configuration options for the W7 NotificationSummarizationClient.
/// Bound from Kendo:Notifications configuration section.
/// Separate from FastApiOptions and FastApiSummarizationOptions to allow
/// independent timeout/breaker tuning for the W7 summarization client.
/// </summary>
public class NotificationSummarizationOptions
{
    /// <summary>FastAPI base URL (Worker calls directly, not through Gateway).</summary>
    public string BaseUrl { get; set; } = "http://fastapi:8000";

    /// <summary>HTTP timeout in seconds for summarization requests.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Number of consecutive failures before circuit breaker opens.</summary>
    public int CircuitBreakerFailures { get; set; } = 3;

    /// <summary>Seconds circuit breaker stays open before half-open probe.</summary>
    public int CircuitBreakerBreakSeconds { get; set; } = 30;
}
