using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace Kendo.Shared.Http;

/// <summary>
/// Worker-specific FastAPI client for W7 notification summarization.
/// Mirrors the Gateway's FastAPIClient pattern but with W7-specific:
/// - 15s timeout (shorter — W7 is a one-shot summarization)
/// - Independent circuit breaker (slow W7 cannot starve W1/W2)
/// - Own resilience pipeline — separate from the Gateway's
/// </summary>
public class FastAPISummarizationClient : IFastAPISummarizationClient
{
    private readonly HttpClient _http;
    private readonly FastApiSummarizationOptions _options;
    private readonly ILogger<FastAPISummarizationClient> _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public FastAPISummarizationClient(
        HttpClient http,
        IOptions<FastApiSummarizationOptions> options,
        ILogger<FastAPISummarizationClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;

        _http.BaseAddress = new Uri(_options.BaseUrl);

        // Build Polly v8 resilience pipeline: timeout → retry → circuit breaker
        var timeoutOptions = new TimeoutStrategyOptions
        {
            Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds)
        };

        var retryOptions = new RetryStrategyOptions<HttpResponseMessage>
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromMilliseconds(100),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                .Handle<TimeoutRejectedException>()
                .Handle<HttpRequestException>()
                .HandleResult(r => r.StatusCode is System.Net.HttpStatusCode.RequestTimeout
                               or System.Net.HttpStatusCode.TooManyRequests
                               or >= System.Net.HttpStatusCode.InternalServerError),
            OnRetry = args =>
            {
                _logger.LogWarning(
                    "FastAPI summarization retry {RetryAttempt}/3 after {DelayMs}ms. Status: {Status}",
                    args.AttemptNumber + 1,
                    args.RetryDelay.TotalMilliseconds,
                    args.Outcome.Result?.StatusCode.ToString() ?? args.Outcome.Exception?.Message);
                return ValueTask.CompletedTask;
            }
        };

        var cbOptions = new CircuitBreakerStrategyOptions<HttpResponseMessage>
        {
            FailureRatio = 1.0,
            MinimumThroughput = _options.CircuitBreakerFailures,
            BreakDuration = TimeSpan.FromSeconds(_options.CircuitBreakerBreakSeconds),
            SamplingDuration = TimeSpan.FromSeconds(_options.CircuitBreakerBreakSeconds * 2),
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                .Handle<TimeoutRejectedException>()
                .Handle<HttpRequestException>()
                .HandleResult(r => (int)r.StatusCode >= 500),
            OnOpened = args =>
            {
                _logger.LogError(
                    "FastAPI summarization circuit breaker OPEN for {BreakDuration}s after {Status}",
                    args.BreakDuration.TotalSeconds,
                    args.Outcome.Result?.StatusCode.ToString() ?? args.Outcome.Exception?.Message);
                return ValueTask.CompletedTask;
            },
            OnClosed = args =>
            {
                _logger.LogInformation("FastAPI summarization circuit breaker CLOSED — service healthy");
                return ValueTask.CompletedTask;
            },
            OnHalfOpened = args =>
            {
                _logger.LogInformation("FastAPI summarization circuit breaker HALF-OPEN — probing");
                return ValueTask.CompletedTask;
            }
        };

        _pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddTimeout(timeoutOptions)
            .AddRetry(retryOptions)
            .AddCircuitBreaker(cbOptions)
            .Build();
    }

    /// <summary>
    /// Calls FastAPI W7 notification summarization endpoint and
    /// yields SSE chunks as they arrive. Each chunk contains the
    /// rendered text and prompt version for auditability.
    /// </summary>
    public async IAsyncEnumerable<SummarizationStreamChunk> SummarizeStreamAsync(
        SummarizationRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        var response = await ExecuteWithPipelineAsync(
            () => _http.PostAsJsonAsync("/v1/notifications/summarize", request, JsonOptions, ct), ct);

        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line)) continue;

            var chunk = JsonSerializer.Deserialize<SummarizationStreamChunk>(line, JsonOptions);
            if (chunk != null)
            {
                yield return chunk;
            }
        }
    }

    private async Task<HttpResponseMessage> ExecuteWithPipelineAsync(
        Func<Task<HttpResponseMessage>> action, CancellationToken ct)
    {
        try
        {
            return await _pipeline.ExecuteAsync(
                async ct2 => await action(), ct);
        }
        catch (BrokenCircuitException)
        {
            _logger.LogError("FastAPI summarization call blocked — circuit breaker OPEN");
            throw new FastAPISummarizationClientException(
                "Service Unavailable",
                "FastAPI summarization circuit breaker is open — service temporarily unavailable",
                503);
        }
        catch (TimeoutRejectedException)
        {
            _logger.LogError("FastAPI summarization call timed out after {Timeout}s", _options.TimeoutSeconds);
            throw new FastAPISummarizationClientException(
                "Gateway Timeout",
                $"FastAPI summarization service did not respond within {_options.TimeoutSeconds}s",
                504);
        }
    }
}

/// <summary>
/// Exception thrown by FastAPISummarizationClient when the
/// resilience pipeline rejects the call (breaker open or timeout).
/// </summary>
public class FastAPISummarizationClientException : Exception
{
    public int StatusCode { get; }
    public string ErrorTitle { get; }

    public FastAPISummarizationClientException(string title, string detail, int statusCode)
        : base(detail)
    {
        ErrorTitle = title;
        StatusCode = statusCode;
    }
}
