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
/// Typed HTTP client for the FastAPI service with Polly v8 resilience:
/// timeout → retry (transient faults only) → circuit breaker.
/// Maps FastAPI RFC 7807 responses to Gateway error types.
/// </summary>
public class FastAPIClient : IFastAPIClient
{
    private readonly HttpClient _http;
    private readonly FastApiOptions _options;
    private readonly FastApiExceptionMapper _mapper;
    private readonly ILogger<FastAPIClient> _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public FastAPIClient(
        HttpClient http,
        IOptions<FastApiOptions> options,
        FastApiExceptionMapper mapper,
        ILogger<FastAPIClient> logger)
    {
        _http = http;
        _options = options.Value;
        _mapper = mapper;
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
                    "FastAPI retry {RetryAttempt}/3 after {DelayMs}ms. Status: {Status}",
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
                    "FastAPI circuit breaker OPEN for {BreakDuration}s after {Status}",
                    args.BreakDuration.TotalSeconds,
                    args.Outcome.Result?.StatusCode.ToString() ?? args.Outcome.Exception?.Message);
                return ValueTask.CompletedTask;
            },
            OnClosed = args =>
            {
                _logger.LogInformation("FastAPI circuit breaker CLOSED — service healthy");
                return ValueTask.CompletedTask;
            },
            OnHalfOpened = args =>
            {
                _logger.LogInformation("FastAPI circuit breaker HALF-OPEN — probing");
                return ValueTask.CompletedTask;
            }
        };

        _pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddTimeout(timeoutOptions)
            .AddRetry(retryOptions)
            .AddCircuitBreaker(cbOptions)
            .Build();
    }

    // W1 — Event Ingestion RAG
    public async Task<EventIngestionResult> IngestEventAsync(
        EventIngestionRequest request, CancellationToken ct)
    {
        var response = await ExecuteWithPipelineAsync(
            () => _http.PostAsJsonAsync("/v1/events/ingest", request, JsonOptions, ct), ct);

        response.EnsureSuccessStatusCode();
        var result = await response.Content
            .ReadFromJsonAsync<EventIngestionResult>(JsonOptions, ct);
        return result!;
    }

    public async IAsyncEnumerable<EventIngestionStreamChunk> IngestEventStreamAsync(
        EventIngestionRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        var response = await ExecuteWithPipelineAsync(
            () => _http.PostAsJsonAsync("/v1/events/ingest/stream", request, JsonOptions, ct), ct);

        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line)) continue;

            var chunk = JsonSerializer.Deserialize<EventIngestionStreamChunk>(line, JsonOptions);
            if (chunk != null)
            {
                yield return chunk;
                if (chunk.IsFinal) yield break;
            }
        }
    }

    // W2 — Event Conflict & Schedule Reasoning
    public async Task<EventValidationResult> ValidateEventAsync(
        Guid eventId, EventValidationRequest request, CancellationToken ct)
    {
        var response = await ExecuteWithPipelineAsync(
            () => _http.PostAsJsonAsync($"/v1/events/{eventId}/validate", request, JsonOptions, ct), ct);

        response.EnsureSuccessStatusCode();
        var result = await response.Content
            .ReadFromJsonAsync<EventValidationResult>(JsonOptions, ct);
        return result!;
    }

    // W3 — User Profile Semantic Search
    public async Task<UserSearchResult> SearchUsersAsync(
        UserSearchRequest request, CancellationToken ct)
    {
        var url = $"/v1/users/search?q={Uri.EscapeDataString(request.Query)}&top={request.Top}";
        var response = await ExecuteWithPipelineAsync(
            () => _http.GetAsync(url, ct), ct);

        response.EnsureSuccessStatusCode();
        var result = await response.Content
            .ReadFromJsonAsync<UserSearchResult>(JsonOptions, ct);
        return result!;
    }

    // W4 — User Intent Classification (advisory)
    public async Task<IntentClassificationResult> ClassifyIntentAsync(
        IntentClassificationRequest request, CancellationToken ct)
    {
        var response = await ExecuteWithPipelineAsync(
            () => _http.PostAsJsonAsync("/v1/intent/classify", request, JsonOptions, ct), ct);

        response.EnsureSuccessStatusCode();
        var result = await response.Content
            .ReadFromJsonAsync<IntentClassificationResult>(JsonOptions, ct);
        return result!;
    }

    // W6 — Document Q&A / Onboarding Assistant
    public async Task<AssistantAnswerResult> AskAssistantAsync(
        AssistantQuestionRequest request, CancellationToken ct)
    {
        var response = await ExecuteWithPipelineAsync(
            () => _http.PostAsJsonAsync("/v1/assistant/ask", request, JsonOptions, ct), ct);

        response.EnsureSuccessStatusCode();
        var result = await response.Content
            .ReadFromJsonAsync<AssistantAnswerResult>(JsonOptions, ct);
        return result!;
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
            _logger.LogError("FastAPI call blocked — circuit breaker OPEN");
            throw new FastApiClientException(FastApiExceptionMapper.CreateCircuitBreakerError());
        }
        catch (TimeoutRejectedException)
        {
            _logger.LogError("FastAPI call timed out after {Timeout}s", _options.TimeoutSeconds);
            throw new FastApiClientException(FastApiExceptionMapper.CreateTimeoutError());
        }
    }
}

public class FastApiClientException : Exception
{
    public FastApiError Error { get; }

    public FastApiClientException(FastApiError error)
        : base($"{error.Title}: {error.Detail}")
    {
        Error = error;
    }
}
