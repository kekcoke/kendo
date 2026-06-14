using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Kendo.Shared.RateLimiting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kendo.Tests.RateLimiting;

[Trait("Category", "Unit")]
public class LoadSheddingTests
{
    private static LoadSheddingMiddleware CreateMiddleware(ConcurrencyLimiter limiter)
    {
        return new LoadSheddingMiddleware(
            _ => Task.CompletedTask,
            limiter,
            NullLogger<LoadSheddingMiddleware>.Instance);
    }

    [Fact]
    public async Task HealthEndpoint_BypassesLoadShedder()
    {
        var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = 1,
            QueueLimit = 0
        });

        // Exhaust the single permit and KEEP it held during the middleware call
        var lease = await limiter.AcquireAsync();
        Assert.True(lease.IsAcquired);

        var middleware = CreateMiddleware(limiter);

        var context = new DefaultHttpContext();
        context.Request.Path = "/health/live";
        context.Request.Method = "GET";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        // Health endpoint passes through even though load shedder is saturated
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);

        lease.Dispose();
    }

    [Fact]
    public async Task WithinLimitRequest_Succeeds()
    {
        var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = 5,
            QueueLimit = 0
        });

        var middleware = CreateMiddleware(limiter);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/users";
        context.Request.Method = "GET";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task LoadShed_Returns503_WhenExceeded()
    {
        var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = 1,
            QueueLimit = 0
        });

        // Exhaust the single permit and KEEP it held during the middleware call
        var lease = await limiter.AcquireAsync();
        Assert.True(lease.IsAcquired);

        var middleware = CreateMiddleware(limiter);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/users";
        context.Request.Method = "GET";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);

        lease.Dispose();
    }

    [Fact]
    public async Task LoadShed_ReturnsProblemJsonContentType()
    {
        var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = 1,
            QueueLimit = 0
        });

        // Exhaust the single permit and KEEP it held during the middleware call
        var lease = await limiter.AcquireAsync();
        Assert.True(lease.IsAcquired);

        var middleware = CreateMiddleware(limiter);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/test";
        context.Request.Method = "POST";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal("application/problem+json; charset=utf-8",
            context.Response.ContentType);

        lease.Dispose();
    }
}
