using Kendo.Shared.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Kendo.Tests.Observability;

[Trait("Category", "Observability")]
public class ObservabilityTests
{
    [Fact]
    public void ResilencePipeline_Logger_IsInjected()
    {
        var options = Options.Create(new ResilienceOptions
        {
            Retry = new RetryOptions { MaxRetries = 1, BaseDelayMs = 10, MaxDelayMs = 100, UseJitter = false },
            CircuitBreaker = new CircuitBreakerOptions { FailureThreshold = 100, BreakDurationSeconds = 1, SamplingDurationSeconds = 60 }
        });

        var loggerMock = new Mock<ILogger<PollyResiliencePipeline>>();
        var pipeline = new PollyResiliencePipeline(options, loggerMock.Object);

        Assert.NotNull(pipeline);
    }

    [Fact]
    public async Task OnRetry_EmitsStructuredLog()
    {
        var options = Options.Create(new ResilienceOptions
        {
            Retry = new RetryOptions { MaxRetries = 2, BaseDelayMs = 10, MaxDelayMs = 100, UseJitter = false },
            CircuitBreaker = new CircuitBreakerOptions { FailureThreshold = 100, BreakDurationSeconds = 1, SamplingDurationSeconds = 60 }
        });

        var loggerMock = new Mock<ILogger<PollyResiliencePipeline>>();
        var pipeline = new PollyResiliencePipeline(options, loggerMock.Object);

        try
        {
            await pipeline.ExecuteAsync(ct =>
                throw new InvalidOperationException("Transient DB error"));
        }
        catch
        {
            // Expected — retries exhausted
        }

        // Should have logged at least one retry attempt
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task OnCircuitBreakerOpened_EmitsErrorLog()
    {
        var options = Options.Create(new ResilienceOptions
        {
            Retry = new RetryOptions { MaxRetries = 1, BaseDelayMs = 10, MaxDelayMs = 100, UseJitter = false },
            CircuitBreaker = new CircuitBreakerOptions { FailureThreshold = 2, BreakDurationSeconds = 30, SamplingDurationSeconds = 60 }
        });

        var loggerMock = new Mock<ILogger<PollyResiliencePipeline>>();
        var pipeline = new PollyResiliencePipeline(options, loggerMock.Object);

        // Trip the circuit breaker with consecutive failures
        for (int i = 0; i < 4; i++)
        {
            try
            {
                await pipeline.ExecuteAsync(ct =>
                    throw new InvalidOperationException("Fail"));
            }
            catch
            {
                // Expected
            }
        }

        // Should have logged at least one error for CB open
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task OnCircuitBreakerClosed_EmitsInfoLog()
    {
        var options = Options.Create(new ResilienceOptions
        {
            Retry = new RetryOptions { MaxRetries = 1, BaseDelayMs = 10, MaxDelayMs = 100, UseJitter = false },
            CircuitBreaker = new CircuitBreakerOptions { FailureThreshold = 2, BreakDurationSeconds = 1, SamplingDurationSeconds = 60 }
        });

        var loggerMock = new Mock<ILogger<PollyResiliencePipeline>>();
        var pipeline = new PollyResiliencePipeline(options, loggerMock.Object);

        // Trip the circuit breaker
        for (int i = 0; i < 4; i++)
        {
            try { await pipeline.ExecuteAsync(ct => throw new InvalidOperationException("Fail")); }
            catch { /* Expected */ }
        }

        // Wait for break duration so CB closes
        await Task.Delay(TimeSpan.FromSeconds(1.5));

        // Execute a successful call — should transition to half-open then closed
        await pipeline.ExecuteAsync(ct => Task.CompletedTask);

        // Should have logged at least one Information for CB closed
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.AtLeastOnce);
    }
}
