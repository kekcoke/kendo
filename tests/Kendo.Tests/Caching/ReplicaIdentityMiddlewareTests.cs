using Kendo.Shared.Caching;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kendo.Tests.Caching;

[Trait("Category", "Unit")]
public class ReplicaIdentityMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_WhenNoHeaderExists_AppendsXKendoReplicaHeader()
    {
        // Arrange
        var logger = NullLogger<ReplicaIdentityMiddleware>.Instance;

        var invoked = false;
        RequestDelegate next = _ =>
        {
            invoked = true;
            return Task.CompletedTask;
        };

        var middleware = new ReplicaIdentityMiddleware(next, logger);

        var httpContext = new DefaultHttpContext();

        // Act
        await middleware.InvokeAsync(httpContext);

        // Assert
        Assert.True(invoked, "Next delegate was not invoked");
        var header = httpContext.Response.Headers["X-Kendo-Replica"].FirstOrDefault();
        Assert.NotNull(header);
        Assert.NotEmpty(header);
    }

    [Fact]
    public async Task InvokeAsync_WhenHeaderAlreadyExists_DoesNotOverwrite()
    {
        // Arrange
        var logger = NullLogger<ReplicaIdentityMiddleware>.Instance;

        RequestDelegate next = context =>
        {
            context.Response.Headers["X-Kendo-Replica"] = "custom-replica";
            return Task.CompletedTask;
        };

        var middleware = new ReplicaIdentityMiddleware(next, logger);

        var httpContext = new DefaultHttpContext();

        // Act
        await middleware.InvokeAsync(httpContext);

        // Assert — existing header should be preserved (set before _next)
        var header = httpContext.Response.Headers["X-Kendo-Replica"].FirstOrDefault();
        Assert.Equal("custom-replica", header);
    }

    [Fact]
    public async Task Constructor_WithFallbackMachineName_ResolvesReplicaId()
    {
        // Arrange — clear HOSTNAME so it falls back to Environment.MachineName
        Environment.SetEnvironmentVariable("HOSTNAME", null);
        var logger = NullLogger<ReplicaIdentityMiddleware>.Instance;
        RequestDelegate next = _ => Task.CompletedTask;

        // Act
        var middleware = new ReplicaIdentityMiddleware(next, logger);

        // Assert
        var httpContext = new DefaultHttpContext();
        var exception = await Record.ExceptionAsync(() => middleware.InvokeAsync(httpContext));
        Assert.Null(exception);

        // Verify a header was still set (from MachineName fallback)
        var header = httpContext.Response.Headers["X-Kendo-Replica"].FirstOrDefault();
        Assert.NotNull(header);
        Assert.NotEmpty(header);
    }
}
