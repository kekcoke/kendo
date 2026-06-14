using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Kendo.Shared.RateLimiting;

/// <summary>
/// Middleware that enforces a fixed-window rate limit per request.
/// When the limit is exceeded, returns 429 Too Many Requests with
/// Retry-After header and an RFC 7807 Problem Details body.
///
/// Health check endpoints (/health/live, /health/ready) are always bypassed.
/// </summary>
public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly FixedWindowRateLimiter _rateLimiter;
    private readonly ILogger<RateLimitingMiddleware> _logger;
    private static readonly PathString HealthLivePath = new("/health/live");
    private static readonly PathString HealthReadyPath = new("/health/ready");

    public RateLimitingMiddleware(
        RequestDelegate next,
        FixedWindowRateLimiter rateLimiter,
        ILogger<RateLimitingMiddleware> logger)
    {
        _next = next;
        _rateLimiter = rateLimiter;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Bypass health endpoints
        if (context.Request.Path.StartsWithSegments(HealthLivePath, StringComparison.OrdinalIgnoreCase) ||
            context.Request.Path.StartsWithSegments(HealthReadyPath, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        using var lease = await _rateLimiter.AcquireAsync(
            permitCount: 1,
            cancellationToken: context.RequestAborted);

        if (lease.IsAcquired)
        {
            await _next(context);
        }
        else
        {
            // Extract Retry-After from lease metadata
            var retryAfter = GetRetryAfter(lease);

            _logger.LogWarning(
                "Rate limit exceeded — {Method} {Path} (retry after {RetryAfter}s)",
                context.Request.Method,
                context.Request.Path,
                retryAfter);

            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;

            if (retryAfter != null)
            {
                context.Response.Headers.RetryAfter = retryAfter.Value.ToString();
            }

            var traceId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "unknown";
            var problemDetails = new
            {
                type = "https://httpstatuses.com/429",
                title = "Too Many Requests",
                status = 429,
                detail = "Rate limit exceeded. Please retry after the Retry-After period.",
                instance = context.Request.Path.Value,
                traceId
            };

            context.Response.ContentType = "application/problem+json; charset=utf-8";
            await System.Text.Json.JsonSerializer.SerializeAsync(
                context.Response.Body, problemDetails, cancellationToken: context.RequestAborted);
        }
    }

    private static int? GetRetryAfter(RateLimitLease lease)
    {
        // Try to extract the retry-after duration from lease metadata
        if (lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
        {
            return (int)Math.Ceiling(retryAfter.TotalSeconds);
        }

        return null;
    }
}
