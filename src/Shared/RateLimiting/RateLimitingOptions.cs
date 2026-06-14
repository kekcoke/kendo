namespace Kendo.Shared.RateLimiting;

public class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public FixedWindowOptions FixedWindow { get; set; } = new();
    public ConcurrencyOptions Concurrency { get; set; } = new();
}

public class FixedWindowOptions
{
    public int PermitLimit { get; set; } = 100;
    public int WindowSeconds { get; set; } = 60;
    public string QueueProcessingOrder { get; set; } = "OldestFirst";
    public int QueueLimit { get; set; } = 10;
}

public class ConcurrencyOptions
{
    public int MaxConcurrency { get; set; } = 50;
    public string QueueProcessingOrder { get; set; } = "OldestFirst";
    public int QueueLimit { get; set; } = 5;
}
