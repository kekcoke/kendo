using System.Diagnostics;
using System.Text.Json;
using Kendo.Shared.ErrorHandling;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kendo.Tests.ErrorHandling;

[Trait("Category", "Unit")]
public class ProblemDetailsMiddlewareTests
{
    private readonly ILogger<ProblemDetailsMiddleware> _logger = NullLogger<ProblemDetailsMiddleware>.Instance;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task HealthLive_SkipsMiddleware_ReturnsPlainText()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/health/live";
        context.Response.Body = new MemoryStream();

        RequestDelegate next = ctx =>
        {
            ctx.Response.StatusCode = 200;
            return ctx.Response.WriteAsync("Healthy");
        };

        var middleware = new ProblemDetailsMiddleware(next, _logger);

        // Act
        await middleware.InvokeAsync(context);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();

        // Assert
        Assert.Equal("Healthy", body);
        Assert.Equal(200, context.Response.StatusCode);
    }

    [Fact]
    public async Task HealthReady_SkipsMiddleware_ReturnsPlainText()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/health/ready";
        context.Response.Body = new MemoryStream();

        RequestDelegate next = ctx =>
        {
            ctx.Response.StatusCode = 200;
            return ctx.Response.WriteAsync("Ready");
        };

        var middleware = new ProblemDetailsMiddleware(next, _logger);

        // Act
        await middleware.InvokeAsync(context);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();

        // Assert
        Assert.Equal("Ready", body);
        Assert.Equal(200, context.Response.StatusCode);
    }

    [Fact]
    public async Task KeyNotFoundException_Returns404ProblemDetails()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/test";
        context.Response.Body = new MemoryStream();

        RequestDelegate next = ctx =>
            throw new KeyNotFoundException("User not found");

        var middleware = new ProblemDetailsMiddleware(next, _logger);

        // Act
        await middleware.InvokeAsync(context);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var problem = JsonSerializer.Deserialize<KendoProblemDetails>(body, JsonOptions);

        // Assert
        Assert.NotNull(problem);
        Assert.Equal("https://httpstatuses.com/404", problem.Type);
        Assert.Equal("Not Found", problem.Title);
        Assert.Equal(404, problem.Status);
        Assert.Contains("User not found", problem.Detail);
        Assert.Equal("/api/test", problem.Instance);
        Assert.NotEmpty(problem.TraceId);
        Assert.Equal("application/problem+json; charset=utf-8", context.Response.ContentType);
    }

    [Fact]
    public async Task ArgumentException_Returns400ProblemDetails()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/test";
        context.Response.Body = new MemoryStream();

        RequestDelegate next = ctx =>
            throw new ArgumentException("Invalid value");

        var middleware = new ProblemDetailsMiddleware(next, _logger);

        // Act
        await middleware.InvokeAsync(context);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var problem = JsonSerializer.Deserialize<KendoProblemDetails>(body, JsonOptions);

        // Assert
        Assert.NotNull(problem);
        Assert.Equal("https://httpstatuses.com/400", problem.Type);
        Assert.Equal("Bad Request", problem.Title);
        Assert.Equal(400, problem.Status);
        Assert.Contains("Invalid value", problem.Detail);
        Assert.Equal("/api/test", problem.Instance);
        Assert.NotEmpty(problem.TraceId);
    }

    [Fact]
    public async Task OperationCanceledException_RequestAborted_Returns499()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/test";
        context.Response.Body = new MemoryStream();

        // Simulate client disconnection
        var cts = new CancellationTokenSource();
        context.RequestAborted = cts.Token;
        cts.Cancel();

        RequestDelegate next = ctx =>
            throw new OperationCanceledException(cts.Token);

        var middleware = new ProblemDetailsMiddleware(next, _logger);

        // Act
        await middleware.InvokeAsync(context);

        // Assert: client-closed requests return 499 without error body
        Assert.Equal(499, context.Response.StatusCode);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Empty(body);
    }

    [Fact]
    public async Task OperationCanceledException_NotRequestAborted_Returns503ProblemDetails()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/test";
        context.Response.Body = new MemoryStream();

        RequestDelegate next = ctx =>
            throw new OperationCanceledException("Timeout");

        var middleware = new ProblemDetailsMiddleware(next, _logger);

        // Act
        await middleware.InvokeAsync(context);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var problem = JsonSerializer.Deserialize<KendoProblemDetails>(body, JsonOptions);

        // Assert
        Assert.NotNull(problem);
        Assert.Equal("https://httpstatuses.com/503", problem.Type);
        Assert.Equal("Service Unavailable", problem.Title);
        Assert.Equal(503, problem.Status);
    }

    [Fact]
    public async Task GenericException_Returns500ProblemDetails()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/test";
        context.Response.Body = new MemoryStream();

        RequestDelegate next = ctx =>
            throw new InvalidOperationException("Something went wrong");

        var middleware = new ProblemDetailsMiddleware(next, _logger);

        // Act
        await middleware.InvokeAsync(context);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var problem = JsonSerializer.Deserialize<KendoProblemDetails>(body, JsonOptions);

        // Assert
        Assert.NotNull(problem);
        Assert.Equal("https://httpstatuses.com/500", problem.Type);
        Assert.Equal("An error occurred while processing your request.", problem.Title);
        Assert.Equal(500, problem.Status);
        Assert.Equal("/api/test", problem.Instance);
        Assert.NotEmpty(problem.TraceId);
    }

    [Fact]
    public async Task TraceId_IsPopulatedFromActivity()
    {
        // Arrange
        using var activity = new Activity("Test").Start();
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/test";
        context.Response.Body = new MemoryStream();

        RequestDelegate next = ctx =>
            throw new KeyNotFoundException("Missing");

        var middleware = new ProblemDetailsMiddleware(next, _logger);

        // Act
        await middleware.InvokeAsync(context);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var problem = JsonSerializer.Deserialize<KendoProblemDetails>(body, JsonOptions);

        // Assert
        Assert.NotNull(problem);
        Assert.Equal(activity.TraceId.ToString(), problem.TraceId);
    }

    [Fact]
    public async Task NoException_PassesThrough()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/test";
        context.Response.Body = new MemoryStream();

        bool nextCalled = false;
        RequestDelegate next = ctx =>
        {
            nextCalled = true;
            ctx.Response.StatusCode = 200;
            return ctx.Response.WriteAsync("OK");
        };

        var middleware = new ProblemDetailsMiddleware(next, _logger);

        // Act
        await middleware.InvokeAsync(context);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();

        // Assert
        Assert.True(nextCalled);
        Assert.Equal("OK", body);
        Assert.Equal(200, context.Response.StatusCode);
    }

    [Fact]
    public async Task ResponseIsApplicationProblemPlusJson()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/test";
        context.Response.Body = new MemoryStream();

        RequestDelegate next = ctx =>
            throw new KeyNotFoundException("Missing");

        var middleware = new ProblemDetailsMiddleware(next, _logger);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.StartsWith("application/problem+json", context.Response.ContentType);
    }
}
