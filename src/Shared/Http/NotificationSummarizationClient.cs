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
/// Calls FastAPI directly (not through Gateway) and properly parses
/// SSE event:/data: line format.
///
/// Has its own independent Polly resilience pipeline — separate from
/// the Gateway's IFastAPIClient and the precedent FastAPISummarizationClient.
/// </summary>
public class NotificationSummarizationClient : INotificationSummarizationClient
{
    private readonly HttpClient _http;
    private readonly NotificationSummarizationOptions _options;
    private readonly ILogger<NotificationSummarizationClient> _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public NotificationSummarizationClient(
        HttpClient http,
        IOptions<NotificationSummarizationOptions> options,
        ILogger<NotificationSummarizationClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;

        // Build Polly v8 resilience pipeline: timeout -> retry -> circuit breaker
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
                    "NotificationSummarization retry {RetryAttempt}/3 after {DelayMs}ms. Status: {Status}",
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
                    "NotificationSummarization circuit breaker OPEN for {BreakDuration}s after {Status}",
                    args.BreakDuration.TotalSeconds,
                    args.Outcome.Result?.StatusCode.ToString() ?? args.Outcome.Exception?.Message);
                return ValueTask.CompletedTask;
            },
            OnClosed = args =>
            {
                _logger.LogInformation("NotificationSummarization circuit breaker CLOSED — service healthy");
                return ValueTask.CompletedTask;
            },
            OnHalfOpened = args =>
            {
                _logger.LogInformation("NotificationSummarization circuit breaker HALF-OPEN — probing");
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
    /// Calls FastAPI /v1/notifications/summarize and yields parsed SSE chunks.
    /// Properly handles event:/data: line format including multi-line data values.
    /// </summary>
    public async IAsyncEnumerable<NotificationSummarizationChunk> SummarizeStreamAsync(
        NotificationSummarizationRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        var response = await ExecuteWithPipelineAsync(
            () => _http.PostAsJsonAsync("/v1/notifications/summarize", request, JsonOptions, ct), ct);

        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        string? currentEvent = null;
        string? currentData = null;

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line == null) break;

            if (line.StartsWith("event: "))
            {
                currentEvent = line[7..];
            }
            else if (line.StartsWith("data: "))
            {
                currentData = line[6..];

                // When we have both event + data, parse and yield
                if (currentEvent != null && currentData != null)
                {
                    var chunk = ParseSseEvent(currentEvent, currentData);
                    if (chunk != null)
                    {
                        yield return chunk;
                    }
                    currentEvent = null;
                    currentData = null;
                }
            }
            // Blank lines separate SSE events — ignore
        }
    }

    private static NotificationSummarizationChunk? ParseSseEvent(string eventType, string data)
    {
        using var doc = JsonDocument.Parse(data);
        var root = doc.RootElement;

        var chunk = new NotificationSummarizationChunk
        {
            Event = eventType
        };

        switch (eventType)
        {
            case "chunk":
                if (root.TryGetProperty("text", out var text))
                    chunk.Text = text.GetString();
                if (root.TryGetProperty("token_count", out var tokenCount))
                    chunk.TokenCount = tokenCount.GetInt32();
                break;

            case "done":
                if (root.TryGetProperty("notification_body", out var body))
                    chunk.NotificationBody = body.GetString();
                if (root.TryGetProperty("prompt_version", out var promptVer))
                    chunk.PromptVersion = promptVer.GetString();
                if (root.TryGetProperty("total_tokens", out var totalTokens))
                    chunk.TotalTokens = totalTokens.GetInt32();
                if (root.TryGetProperty("trace_id", out var traceId))
                    chunk.TraceId = traceId.GetString();
                break;

            case "error":
                if (root.TryGetProperty("title", out var title))
                    chunk.Title = title.GetString();
                if (root.TryGetProperty("status", out var status))
                    chunk.Status = status.GetInt32();
                if (root.TryGetProperty("detail", out var detail))
                    chunk.Detail = detail.GetString();
                break;
        }

        return chunk;
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
            _logger.LogError("NotificationSummarization call blocked — circuit breaker OPEN");
            throw new NotificationSummarizationClientException(
                "Service Unavailable",
                "FastAPI notification summarization circuit breaker is open — service temporarily unavailable",
                503);
        }
        catch (TimeoutRejectedException)
        {
            _logger.LogError("NotificationSummarization call timed out after {Timeout}s", _options.TimeoutSeconds);
            throw new NotificationSummarizationClientException(
                "Gateway Timeout",
                $"FastAPI notification summarization service did not respond within {_options.TimeoutSeconds}s",
                504);
        }
    }
}

/// <summary>
/// Exception thrown by NotificationSummarizationClient when the
/// resilience pipeline rejects the call (breaker open or timeout).
/// Conveys RFC 7807-compatible error data for Worker DLQ routing.
/// </summary>
public class NotificationSummarizationClientException : Exception
{
    public int StatusCode { get; }
    public string ErrorTitle { get; }

    public NotificationSummarizationClientException(string title, string detail, int statusCode)
        : base(detail)
    {
        ErrorTitle = title;
        StatusCode = statusCode;
    }
}
