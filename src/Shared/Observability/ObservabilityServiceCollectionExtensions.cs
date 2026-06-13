using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Kendo.Shared.Observability;

public static class ObservabilityServiceCollectionExtensions
{
    public static IServiceCollection AddKendoObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddConsoleExporter());

        return services;
    }
}
