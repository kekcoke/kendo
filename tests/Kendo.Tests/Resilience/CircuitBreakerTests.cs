using Kendo.Shared.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Kendo.Tests.Resilience;

[Trait("Category", "Resilience")]
public class CircuitBreakerTests
{
    private static ResilienceOptions CreateOptions(int failureThreshold = 3, int breakDurationSeconds = 2)
    {
        return new ResilienceOptions
        {
            Retry = new RetryOptions { MaxRetries = 1 }, // Minimal retry for CB-only tests
            CircuitBreaker = new CircuitBreakerOptions
            {
                FailureThreshold = failureThreshold,
                BreakDurationSeconds = breakDurationSeconds,
                SamplingDurationSeconds = 30
            }
        };
    }

    [Fact]
    public async Task CircuitBreaker_Trips_AfterConsecutiveFailures()
    {
        var options = Options.Create(CreateOptions(failureThreshold: 3, breakDurationSeconds: 30));
        var pipeline = new PollyResiliencePipeline(options, Mock.Of<ILogger<PollyResiliencePipeline>>());
        var failures = 0;

        // Execute failures up to and past the threshold
        for (int i = 0; i < 5; i++)
        {
            try
            {
                await pipeline.ExecuteAsync(ct =>
                {
                    failures++;
                    throw new InvalidOperationException("Simulated failure");
                });
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex.GetType().Name == "BrokenCircuitException")
            {
                // Expected — CB should trip after 3 consecutive failures
            }
        }

        // Should have attempted at most threshold times before CB opens
        Assert.True(failures <= 4, $"Expected ≤ 4 failures (3 before trip + 1 half-open), got {failures}");
    }

    [Fact]
    public async Task CircuitBreaker_Resets_AfterBreakDuration()
    {
        var options = Options.Create(CreateOptions(failureThreshold: 2, breakDurationSeconds: 1));
        var pipeline = new PollyResiliencePipeline(options, Mock.Of<ILogger<PollyResiliencePipeline>>());

        // Trip the circuit breaker
        for (int i = 0; i < 3; i++)
        {
            try
            {
                await pipeline.ExecuteAsync(ct => throw new InvalidOperationException("Fail"));
            }
            catch { /* Expected */ }
        }

        // Wait for break duration to expire
        await Task.Delay(TimeSpan.FromSeconds(1.5));

        // Circuit should now allow requests (half-open → closed on success)
        var success = false;
        await pipeline.ExecuteAsync(ct =>
        {
            success = true;
            return Task.CompletedTask;
        });

        Assert.True(success, "Circuit breaker should allow requests after break duration");
    }

    [Fact]
    public async Task CircuitBreaker_ThrowsBrokenCircuit_WhenOpen()
    {
        var options = Options.Create(CreateOptions(failureThreshold: 2, breakDurationSeconds: 30));
        var pipeline = new PollyResiliencePipeline(options, Mock.Of<ILogger<PollyResiliencePipeline>>());

        // Trip the circuit breaker with a single failure
        try { await pipeline.ExecuteAsync(ct => throw new InvalidOperationException("Fail")); }
        catch { /* Expected — retry exhausted or CB opened */ }

        // Circuit should now be open — verify by checking that the next call fails
        var threw = false;
        try
        {
            await pipeline.ExecuteAsync(ct => Task.CompletedTask);
        }
        catch (Exception ex)
        {
            threw = true;
            Assert.Contains("BrokenCircuit", ex.GetType().Name);
        }

        Assert.True(threw, "Expected circuit breaker to throw when open");
    }

    [Fact]
    public async Task CircuitBreaker_Generic_ReturnsResult()
    {
        var options = Options.Create(CreateOptions(failureThreshold: 3, breakDurationSeconds: 30));
        var pipeline = new PollyResiliencePipeline(options, Mock.Of<ILogger<PollyResiliencePipeline>>());

        var result = await pipeline.ExecuteAsync(ct => Task.FromResult(42));

        Assert.Equal(42, result);
    }
}
