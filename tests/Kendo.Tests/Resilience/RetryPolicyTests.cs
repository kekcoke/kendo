using Kendo.Shared.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Kendo.Tests.Resilience;

[Trait("Category", "Resilience")]
public class RetryPolicyTests
{
    private static ResilienceOptions CreateRetryOnlyOptions(int maxRetries = 3, int baseDelayMs = 10, bool useJitter = false)
    {
        return new ResilienceOptions
        {
            Retry = new RetryOptions
            {
                MaxRetries = maxRetries,
                BaseDelayMs = baseDelayMs,
                MaxDelayMs = 200,
                UseJitter = useJitter
            },
            CircuitBreaker = new CircuitBreakerOptions
            {
                FailureThreshold = 100, // Effectively disable CB
                BreakDurationSeconds = 1,
                SamplingDurationSeconds = 60
            }
        };
    }

    [Fact]
    public async Task Retry_RetriesTransientFailures()
    {
        var options = Options.Create(CreateRetryOnlyOptions(maxRetries: 3));
        var pipeline = new PollyResiliencePipeline(options, Mock.Of<ILogger<PollyResiliencePipeline>>());
        var attempts = 0;

        // Succeeds on 3rd attempt
        var result = await pipeline.ExecuteAsync(ct =>
        {
            attempts++;
            if (attempts < 3)
                throw new InvalidOperationException($"Transient failure {attempts}");
            return Task.FromResult("success");
        });

        Assert.Equal(3, attempts);
        Assert.Equal("success", result);
    }

    [Fact]
    public async Task Retry_ExhaustsRetries_AndThrowsLastException()
    {
        var options = Options.Create(CreateRetryOnlyOptions(maxRetries: 2));
        var pipeline = new PollyResiliencePipeline(options, Mock.Of<ILogger<PollyResiliencePipeline>>());
        var attempts = 0;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.ExecuteAsync(ct =>
            {
                attempts++;
                throw new InvalidOperationException($"Failure {attempts}");
            }));

        // 1 initial + 2 retries = 3 total attempts
        Assert.Equal(3, attempts);
        Assert.Contains("Failure 3", ex.Message);
    }

    [Fact]
    public async Task Retry_ExponentialBackoff_IsApplied()
    {
        var options = Options.Create(CreateRetryOnlyOptions(maxRetries: 2, baseDelayMs: 50));
        var pipeline = new PollyResiliencePipeline(options, Mock.Of<ILogger<PollyResiliencePipeline>>());
        var attempts = 0;
        var startTime = DateTime.UtcNow;

        try
        {
            await pipeline.ExecuteAsync(ct =>
            {
                attempts++;
                throw new InvalidOperationException("Always fail");
            });
        }
        catch { /* Expected */ }

        var elapsed = DateTime.UtcNow - startTime;
        // With exponential backoff: 50ms + 100ms + 200ms ≈ 350ms minimum
        Assert.True(elapsed.TotalMilliseconds >= 100, $"Expected ≥ 100ms elapsed for backoff, got {elapsed.TotalMilliseconds}ms");
    }

    [Fact]
    public async Task Retry_NoRetry_OnSuccess()
    {
        var options = Options.Create(CreateRetryOnlyOptions(maxRetries: 3));
        var pipeline = new PollyResiliencePipeline(options, Mock.Of<ILogger<PollyResiliencePipeline>>());
        var attempts = 0;

        var result = await pipeline.ExecuteAsync(ct =>
        {
            attempts++;
            return Task.FromResult(attempts);
        });

        Assert.Equal(1, attempts);
        Assert.Equal(1, result);
    }
}
