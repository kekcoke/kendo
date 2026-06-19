namespace Kendo.Shared.Http;

/// <summary>
/// Configuration options for the FastAPI service client.
/// Bound from Kendo:FastApi configuration section.
/// </summary>
public class FastApiOptions
{
    public string BaseUrl { get; set; } = "http://fastapi:8000";
    public int TimeoutSeconds { get; set; } = 30;
    public int CircuitBreakerFailures { get; set; } = 3;
    public int CircuitBreakerBreakSeconds { get; set; } = 30;
}
