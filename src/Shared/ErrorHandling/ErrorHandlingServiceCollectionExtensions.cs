using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Kendo.Shared.ErrorHandling;

/// <summary>
/// Extension methods for registering and enabling the RFC 7807 ProblemDetails middleware.
/// </summary>
public static class ErrorHandlingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the ProblemDetails middleware services.
    /// Call in service configuration (builder.Services).
    /// </summary>
    public static IServiceCollection AddKendoErrorHandling(this IServiceCollection services)
    {
        return services;
    }

    /// <summary>
    /// Adds the ProblemDetails middleware to the request pipeline.
    /// Call after UseRouting() and before UseAuthorization() / MapControllers().
    /// </summary>
    public static IApplicationBuilder UseKendoErrorHandling(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ProblemDetailsMiddleware>();
    }
}
