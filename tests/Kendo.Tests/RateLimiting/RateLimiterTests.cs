using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Kendo.Shared.RateLimiting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kendo.Tests.RateLimiting;

[Trait("Category", "Unit")]
public class RateLimiterTests
{
    private static RateLimitingMiddleware CreateMiddleware(FixedWindowRateLimiter limiter)
    {
        return new RateLimitingMiddleware(
            _ => Task.CompletedTask,
            limiter,
            NullLogger<RateLimitingMiddleware>.Instance);
    }

    [Fact]
    public async Task HealthEndpoint_BypassesRateLimiter()
    {
        var limiter = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = 1,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = false
        });

        var middleware = CreateMiddleware(limiter);

        var context = new DefaultHttpContext();
        context.Request.Path = "/health/live";
        context.Request.Method = "GET";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        // Health endpoint passes through even though rate limiter is exhausted
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task RateLimited_Returns429_WhenExceeded()
    {
        // Create a limiter with 1 permit and exhaust it
        var limiter = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = 1,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = false
        });

        // Exhaust the single permit
        using (var lease = await limiter.AcquireAsync())
        {
            Assert.True(lease.IsAcquired);
        }

        var middleware = CreateMiddleware(limiter);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/test";
        context.Request.Method = "GET";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
        Assert.True(context.Response.Headers.ContainsKey("Retry-After"));
    }

    [Fact]
    public async Task RateLimitedResponse_HasProblemJsonContentType()
    {
        var limiter = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = 1,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = false
        });

        // Exhaust the single permit
        using (var lease = await limiter.AcquireAsync())
        {
            Assert.True(lease.IsAcquired);
        }

        var middleware = CreateMiddleware(limiter);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/users";
        context.Request.Method = "POST";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal("application/problem+json; charset=utf-8",
            context.Response.ContentType);
    }

    [Fact]
    public async Task WithinLimitRequest_PassesThrough()
    {
        var limiter = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });

        var middleware = CreateMiddleware(limiter);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/test";
        context.Request.Method = "GET";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }
}
