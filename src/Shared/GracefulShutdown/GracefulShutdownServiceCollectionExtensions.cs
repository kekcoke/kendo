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

        // Register middleware
        services.AddSingleton<GracefulShutdownMiddleware>();

        // Configure host shutdown timeout
        services.Configure<HostOptions>(options =>
        {
            options.ShutdownTimeout = timeout;
        });

        return services;
    }
}
