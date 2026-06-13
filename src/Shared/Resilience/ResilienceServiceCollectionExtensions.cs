using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Kendo.Shared.Resilience;

public static class ResilienceServiceCollectionExtensions
{
    public static IServiceCollection AddKendoResilience(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ResilienceOptions>(
            configuration.GetSection(ResilienceOptions.SectionName));

        services.AddSingleton<IResiliencePipeline, PollyResiliencePipeline>();

        return services;
    }
}
