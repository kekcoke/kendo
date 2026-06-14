using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace Kendo.Shared.GracefulShutdown;

/// <summary>
/// Middleware that tracks in-flight requests and blocks new requests
/// during graceful shutdown (after IHostApplicationLifetime.ApplicationStopping).
/// Health check endpoints are always bypassed.
/// </summary>
public class GracefulShutdownMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RequestTracker _tracker;
    private static readonly PathString HealthLivePath = new("/health/live");
    private static readonly PathString HealthReadyPath = new("/health/ready");

    public GracefulShutdownMiddleware(RequestDelegate next, RequestTracker tracker)
    {
        _next = next;
        _tracker = tracker;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Bypass health endpoints — they must respond even during drain
        if (context.Request.Path == HealthLivePath ||
            context.Request.Path == HealthReadyPath)
        {
            await _next(context);
            return;
        }

        if (_tracker.IsDraining)
        {
            context.Response.StatusCode = 503;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsync(
                """{"type":"https://httpstatuses.com/503","title":"Service Unavailable","detail":"Server is shutting down — no new requests accepted","status":503}""");
            return;
        }

        using var scope = _tracker.BeginRequest();
        await _next(context);
    }
}
