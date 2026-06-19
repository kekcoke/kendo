using System.Net;
using System.Text.Json;

namespace Kendo.Shared.Http;

/// <summary>
/// Translates FastAPI's RFC 7807 problem+json responses to
/// consistent KendoProblemDetails for Gateway consumers.
/// Falls back to a generic 502 on non-RFC-7807 responses.
/// </summary>
public class FastApiExceptionMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public FastApiError MapToGatewayError(HttpResponseMessage response, string? body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return new FastApiError(
                StatusCode: (int)response.StatusCode,
                Title: "Upstream service error",
                Detail: $"FastAPI returned {response.StatusCode} with no body",
                TraceId: null
            );
        }

        try
        {
            var problem = JsonSerializer.Deserialize<FastApiProblemResponse>(body, JsonOptions);
            if (problem is not null)
            {
                return new FastApiError(
                    StatusCode: problem.Status ?? (int)response.StatusCode,
                    Title: problem.Title ?? "Upstream service error",
                    Detail: problem.Detail ?? body,
                    TraceId: problem.TraceId
                );
            }
        }
        catch (JsonException)
        {
            // Not a RFC 7807 body — fall through to generic
        }

        return new FastApiError(
            StatusCode: (int)response.StatusCode,
            Title: "Upstream service error",
            Detail: body,
            TraceId: null
        );
    }

    public static FastApiError CreateTimeoutError()
    {
        return new FastApiError(
            StatusCode: 504,
            Title: "Gateway Timeout",
            Detail: "FastAPI service did not respond within the configured timeout",
            TraceId: null
        );
    }

    public static FastApiError CreateCircuitBreakerError()
    {
        return new FastApiError(
            StatusCode: 503,
            Title: "Service Unavailable",
            Detail: "FastAPI circuit breaker is open — service temporarily unavailable",
            TraceId: null
        );
    }
}

public record FastApiError(int StatusCode, string Title, string Detail, string? TraceId);

internal record FastApiProblemResponse
{
    public string? Type { get; init; }
    public string? Title { get; init; }
    public int? Status { get; init; }
    public string? Detail { get; init; }
    public string? Instance { get; init; }
    public string? TraceId { get; init; }
}
