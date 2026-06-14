using Microsoft.AspNetCore.Builder;

namespace Kendo.Shared.RateLimiting;

/// <summary>
/// Extension methods for registering rate limiting and load shedding middleware
/// in the ASP.NET Core pipeline.
/// </summary>
public static class RateLimitingApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the <see cref="RateLimitingMiddleware"/> to the request pipeline.
    /// Enforces a fixed-window rate limit; returns 429 when exceeded.
    /// 
    /// Should be registered after error handling middleware to ensure
    /// 429 responses flow through the RFC 7807 serialization.
    /// </summary>
    public static IApplicationBuilder UseKendoRateLimiter(this IApplicationBuilder app)
    {
        return app.UseMiddleware<RateLimitingMiddleware>();
    }

    /// <summary>
    /// Adds the <see cref="LoadSheddingMiddleware"/> to the request pipeline.
    /// Limits concurrent in-flight requests; returns 503 when saturated.
    /// 
    /// Should be registered before rate limiting middleware to shed load
    /// before counting a request against the rate limit.
    /// </summary>
    public static IApplicationBuilder UseKendoLoadShedding(this IApplicationBuilder app)
    {
        return app.UseMiddleware<LoadSheddingMiddleware>();
    }
}
