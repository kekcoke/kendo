using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kendo.Shared.Http;

/// <summary>
/// Registers the IFastAPIClient typed HTTP client with the FastAPI
/// base URL and Polly resilience pipeline.
/// </summary>
public static class AddKendoFastApiClientExtensions
{
    public static IServiceCollection AddKendoFastApiClient(
        this IServiceCollection services, IConfiguration config)
    {
        services.Configure<FastApiOptions>(config.GetSection("Kendo:FastApi"));
        services.AddSingleton<FastApiExceptionMapper>();

        services.AddHttpClient<IFastAPIClient, FastAPIClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<FastApiOptions>>();
            client.BaseAddress = new Uri(options.Value.BaseUrl);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        return services;
    }
}
