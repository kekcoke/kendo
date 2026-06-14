using Kendo.Shared.GracefulShutdown;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Kendo.Tests.GracefulShutdown;

[Trait("Category", "GracefulShutdown")]
[Trait("Category", "Unit")]
public class GracefulShutdownMiddlewareTests
{
    // ── RequestTracker tests ──────────────────────────────────────────

    [Fact]
    public void RequestTracker_AllowsRequestWhenNotDraining()
    {
        var tracker = new RequestTracker();
        Assert.False(tracker.IsDraining);
        Assert.Equal(0, tracker.InFlightCount);

        using var scope = tracker.BeginRequest();
        Assert.Equal(1, tracker.InFlightCount);

        Assert.True(scope is IDisposable);
    }

    [Fact]
    public void RequestTracker_TracksMultipleConcurrentRequests()
    {
        var tracker = new RequestTracker();

        var scope1 = tracker.BeginRequest();
        var scope2 = tracker.BeginRequest();
        var scope3 = tracker.BeginRequest();

        Assert.Equal(3, tracker.InFlightCount);

        scope3.Dispose();
        Assert.Equal(2, tracker.InFlightCount);

        scope2.Dispose();
        Assert.Equal(1, tracker.InFlightCount);

        scope1.Dispose();
        Assert.Equal(0, tracker.InFlightCount);
    }

    [Fact]
    public void RequestTracker_BlocksRequestDuringDrain()
    {
        var tracker = new RequestTracker();
        tracker.StartDraining();
        Assert.True(tracker.IsDraining);

        Assert.Throws<InvalidOperationException>(() => tracker.BeginRequest());
    }

    [Fact]
    public void RequestTracker_WaitForDrain_ReturnsTrueWhenCountZero()
    {
        var tracker = new RequestTracker();

        var result = tracker.WaitForDrain(TimeSpan.FromMilliseconds(100));

        Assert.True(result);
    }

    [Fact]
    public void RequestTracker_WaitForDrain_BlocksUntilDrainComplete()
    {
        var tracker = new RequestTracker();
        using var scope = tracker.BeginRequest();

        // Start drain in background after a short delay
        var drainTask = Task.Run(() => tracker.WaitForDrain(TimeSpan.FromSeconds(5)));

        // Give drain a moment to start waiting
        Thread.Sleep(200);

        // End the in-flight request
        scope.Dispose();

        // Drain should complete
        var drainResult = drainTask.GetAwaiter().GetResult();
        Assert.True(drainResult);
    }

    [Fact]
    public void RequestTracker_WaitForDrain_TimeoutReturnsFalse()
    {
        var tracker = new RequestTracker();
        using var scope = tracker.BeginRequest(); // hold one request

        var result = tracker.WaitForDrain(TimeSpan.FromMilliseconds(50));

        Assert.False(result);
    }

    [Fact]
    public void StartDraining_IsIdempotent()
    {
        var tracker = new RequestTracker();

        tracker.StartDraining();
        Assert.True(tracker.IsDraining);
        // Calling again should not throw
        tracker.StartDraining();
        Assert.True(tracker.IsDraining);
    }

    // ── GracefulShutdownMiddleware tests ───────────────────────────────

    private static GracefulShutdownMiddleware CreateMiddleware(RequestTracker tracker, RequestDelegate? next = null)
    {
        next ??= _ => Task.CompletedTask;
        return new GracefulShutdownMiddleware(next, tracker);
    }

    private static DefaultHttpContext CreateContext(string path = "/api/test")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = "GET";
        context.Response.Body = new MemoryStream();
        return context;
    }

    [Fact]
    public async Task Middleware_BypassesHealthEndpoints_WhenNotDraining()
    {
        var tracker = new RequestTracker();
        var middleware = CreateMiddleware(tracker);

        var liveContext = CreateContext("/health/live");
        await middleware.InvokeAsync(liveContext);
        Assert.Equal(StatusCodes.Status200OK, liveContext.Response.StatusCode);

        var readyContext = CreateContext("/health/ready");
        await middleware.InvokeAsync(readyContext);
        Assert.Equal(StatusCodes.Status200OK, readyContext.Response.StatusCode);
    }

    [Fact]
    public async Task Middleware_BypassesHealthEndpoints_WhenDraining()
    {
        var tracker = new RequestTracker();
        tracker.StartDraining();
        var middleware = CreateMiddleware(tracker);

        var liveContext = CreateContext("/health/live");
        await middleware.InvokeAsync(liveContext);
        Assert.Equal(StatusCodes.Status200OK, liveContext.Response.StatusCode);

        var readyContext = CreateContext("/health/ready");
        await middleware.InvokeAsync(readyContext);
        Assert.Equal(StatusCodes.Status200OK, readyContext.Response.StatusCode);
    }

    [Fact]
    public async Task Middleware_ProxiesNormalRequests_WhenNotDraining()
    {
        var tracker = new RequestTracker();
        var middleware = CreateMiddleware(tracker);

        var context = CreateContext("/api/users");

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(0, tracker.InFlightCount); // scope disposed
    }

    [Fact]
    public async Task Middleware_Returns503_WhenDraining()
    {
        var tracker = new RequestTracker();
        tracker.StartDraining();
        var middleware = CreateMiddleware(tracker);

        var context = CreateContext("/api/users");

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
    }

    [Fact]
    public async Task GracefulShutdownMiddleware_SetsCorrect503Body()
    {
        var tracker = new RequestTracker();
        tracker.StartDraining();
        var middleware = CreateMiddleware(tracker);

        var context = CreateContext("/api/users");
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal("application/problem+json", context.Response.ContentType);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();

        Assert.Contains("503", body);
        Assert.Contains("Service Unavailable", body);
        Assert.Contains("shutting down", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Middleware_TracksInFlightRequests()
    {
        var tracker = new RequestTracker();
        var callTracker = 0;

        async Task Next(HttpContext ctx)
        {
            Interlocked.Increment(ref callTracker);
            // Simulate work — tracker should have 1 in-flight
            Assert.Equal(1, tracker.InFlightCount);
            await Task.Delay(50);
        }

        var middleware = new GracefulShutdownMiddleware(Next, tracker);

        var context = CreateContext("/api/users");

        await middleware.InvokeAsync(context);

        Assert.Equal(1, callTracker);
        Assert.Equal(0, tracker.InFlightCount); // scope disposed after next returns
    }
}
