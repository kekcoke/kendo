using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Kendo.Shared.Caching;

public class ReplicaIdentityMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _replicaId;
    private readonly ILogger<ReplicaIdentityMiddleware> _logger;

    public ReplicaIdentityMiddleware(RequestDelegate next, ILogger<ReplicaIdentityMiddleware> logger)
    {
        _next = next;
        _logger = logger;

        _replicaId = Environment.GetEnvironmentVariable("HOSTNAME")
                     ?? Environment.MachineName
                     ?? "unknown";

        _logger.LogInformation("Replica identity resolved: {ReplicaId}", _replicaId);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Response.Headers.ContainsKey("X-Kendo-Replica"))
        {
            context.Response.Headers["X-Kendo-Replica"] = _replicaId;
        }

        await _next(context);
    }
}

public static class ReplicaIdentityMiddlewareExtensions
{
    public static IApplicationBuilder UseKendoReplicaIdentity(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ReplicaIdentityMiddleware>();
    }
}
