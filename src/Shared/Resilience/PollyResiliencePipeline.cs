using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using Polly.CircuitBreaker;

namespace Kendo.Shared.Resilience;

public class PollyResiliencePipeline : IResiliencePipeline, IDisposable
{
    private readonly ResiliencePipeline _pipeline;
    private readonly ILogger<PollyResiliencePipeline> _logger;

    public PollyResiliencePipeline(
        IOptions<ResilienceOptions> options,
        ILogger<PollyResiliencePipeline> logger)
    {
        _logger = logger;
        var opts = options.Value;

        var retryOptions = new RetryStrategyOptions
        {
            MaxRetryAttempts = opts.Retry.MaxRetries,
            Delay = TimeSpan.FromMilliseconds(opts.Retry.BaseDelayMs),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = opts.Retry.UseJitter,
            ShouldHandle = new PredicateBuilder()
                .Handle<Exception>(),
            OnRetry = args =>
            {
                _logger.LogWarning(
                    "Retry attempt {Attempt}/{MaxRetries} after {Delay}ms — {ExceptionMessage}",
                    args.AttemptNumber + 1,
                    opts.Retry.MaxRetries,
                    opts.Retry.BaseDelayMs * (1 << args.AttemptNumber),
                    args.Outcome.Exception?.Message);
                return ValueTask.CompletedTask;
            }
        };

        var cbOptions = new CircuitBreakerStrategyOptions
        {
            FailureRatio = 1.0,
            MinimumThroughput = opts.CircuitBreaker.FailureThreshold,
            BreakDuration = TimeSpan.FromSeconds(opts.CircuitBreaker.BreakDurationSeconds),
            SamplingDuration = TimeSpan.FromSeconds(opts.CircuitBreaker.SamplingDurationSeconds),
            ShouldHandle = new PredicateBuilder()
                .Handle<Exception>(),
            OnOpened = args =>
            {
                _logger.LogError(
                    "Circuit breaker OPENED — {FailureThreshold} failures in {SamplingDuration}s",
                    opts.CircuitBreaker.FailureThreshold,
                    opts.CircuitBreaker.SamplingDurationSeconds);
                return ValueTask.CompletedTask;
            },
            OnClosed = args =>
            {
                _logger.LogInformation(
                    "Circuit breaker CLOSED — normal operation resumed");
                return ValueTask.CompletedTask;
            },
            OnHalfOpened = args =>
            {
                _logger.LogInformation(
                    "Circuit breaker HALF-OPENED — probe request allowed");
                return ValueTask.CompletedTask;
            }
        };

        _pipeline = new ResiliencePipelineBuilder()
            .AddRetry(retryOptions)
            .AddCircuitBreaker(cbOptions)
            .Build();
    }

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct = default)
    {
        return await _pipeline.ExecuteAsync(
            ct2 => new ValueTask<T>(action(ct2)),
            ct);
    }

    public async Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken ct = default)
    {
        await _pipeline.ExecuteAsync(
            ct2 => new ValueTask(action(ct2)),
            ct);
    }

    public void Dispose()
    {
        // ResiliencePipeline doesn't need manual disposal in Polly.Core v8.x
    }
}
