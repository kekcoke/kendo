using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kendo.Shared.Http;

/// <summary>
/// Registers the IFastAPISummarizationClient typed HTTP client with
/// the FastAPI base URL and independent Polly resilience pipeline.
/// 
/// This is the Worker's W7 client — separate from the Gateway's
/// IFastAPIClient to avoid pulling in W1–W6 DTOs and to maintain
/// independent circuit breakers.
/// </summary>
public static class AddKendoFastApiSummarizationClientExtensions
{
    public static IServiceCollection AddKendoFastApiSummarizationClient(
        this IServiceCollection services, IConfiguration config)
    {
        services.Configure<FastApiSummarizationOptions>(
            config.GetSection("Kendo:FastApi:Summarization"));

        services.AddHttpClient<IFastAPISummarizationClient, FastAPISummarizationClient>(
            (sp, client) =>
            {
                var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<FastApiSummarizationOptions>>();
                client.BaseAddress = new Uri(options.Value.BaseUrl);
                client.DefaultRequestHeaders.Add("Accept", "application/json");
            });

        return services;
    }
}
