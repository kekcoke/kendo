using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kendo.Shared.RateLimiting;

/// <summary>
/// Middleware that limits concurrent in-flight requests using a semaphore-based
/// <see cref="ConcurrencyLimiter"/>. When the limit is exceeded, returns 503
/// with an RFC 7807 Problem Details body.
///
/// Always bypassed for health check endpoints (/health/live, /health/ready).
/// Must be registered before <see cref="RateLimitingMiddleware"/> in the pipeline.
/// </summary>
public class LoadSheddingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ConcurrencyLimiter _limiter;
    private readonly ILogger<LoadSheddingMiddleware> _logger;
    private static readonly PathString HealthLivePath = new("/health/live");
    private static readonly PathString HealthReadyPath = new("/health/ready");

    public LoadSheddingMiddleware(
        RequestDelegate next,
        ConcurrencyLimiter limiter,
        ILogger<LoadSheddingMiddleware> logger)
    {
        _next = next;
        _limiter = limiter;
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

        using var lease = await _limiter.AcquireAsync(
            permitCount: 1,
            cancellationToken: context.RequestAborted);

        if (lease.IsAcquired)
        {
            try
            {
                await _next(context);
            }
            finally
            {
                // lease is disposed, release happens automatically via Dispose
            }
        }
        else
        {
            _logger.LogWarning(
                "Load shedding — max concurrency ({MaxConcurrency}) reached for {Method} {Path}",
                _limiter.GetStatistics().CurrentQueuedCount,
                context.Request.Method,
                context.Request.Path);

            // Return 503 RFC 7807
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;

            var traceId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "unknown";
            var problemDetails = new
            {
                type = "https://httpstatuses.com/503",
                title = "Service Unavailable",
                status = 503,
                detail = "Server is at maximum capacity. Please retry later.",
                instance = context.Request.Path.Value,
                traceId
            };

            context.Response.ContentType = "application/problem+json; charset=utf-8";
            await System.Text.Json.JsonSerializer.SerializeAsync(
                context.Response.Body, problemDetails, cancellationToken: context.RequestAborted);
        }
    }
}
