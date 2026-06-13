namespace Kendo.Shared.Resilience;

public class ResilienceOptions
{
    public const string SectionName = "Resilience";

    public RetryOptions Retry { get; set; } = new();
    public CircuitBreakerOptions CircuitBreaker { get; set; } = new();
}

public class RetryOptions
{
    public int MaxRetries { get; set; } = 3;
    public int BaseDelayMs { get; set; } = 100;
    public int MaxDelayMs { get; set; } = 2000;
    public bool UseJitter { get; set; } = true;
}

public class CircuitBreakerOptions
{
    public int FailureThreshold { get; set; } = 3;
    public int BreakDurationSeconds { get; set; } = 30;
    public int SamplingDurationSeconds { get; set; } = 30;
}
