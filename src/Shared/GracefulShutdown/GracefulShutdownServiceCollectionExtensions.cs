using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kendo.Shared.GracefulShutdown;

public static class GracefulShutdownServiceCollectionExtensions
{
    private const string TimeoutKey = "GracefulShutdown__TimeoutSeconds";

    public static IServiceCollection AddKendoGracefulShutdown(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var timeoutSeconds = configuration.GetValue<int?>(TimeoutKey) ?? 30;
        var timeout = TimeSpan.FromSeconds(timeoutSeconds);

        // Register singleton tracker
        services.AddSingleton<RequestTracker>();

        // Middleware is NOT registered as a service — ASP.NET Core's UseMiddleware<T>()
        // provides RequestDelegate at pipeline construction time.

        // Register hosted service that triggers drain on SIGTERM
        services.AddHostedService(sp =>
        {
            var tracker = sp.GetRequiredService<RequestTracker>();
            return new GracefulShutdownHostedService(tracker, timeout);
        });

        // Configure host shutdown timeout
        services.Configure<HostOptions>(options =>
        {
            options.ShutdownTimeout = timeout;
        });

        return services;
    }
}
