using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kendo.Shared.Http;

/// <summary>
/// Registers the INotificationSummarizationClient typed HTTP client for W7.
/// Uses its own configuration section (Kendo:Notifications) independent from
/// the Gateway's IFastAPIClient and the Worker's IFastAPISummarizationClient.
/// This ensures W7's circuit breaker and timeout are independently tunable.
/// </summary>
public static class AddKendoNotificationSummarizationExtensions
{
    public static IServiceCollection AddKendoNotificationSummarization(
        this IServiceCollection services, IConfiguration config)
    {
        services.Configure<NotificationSummarizationOptions>(
            config.GetSection("Kendo:Notifications"));

        services.AddHttpClient<INotificationSummarizationClient, NotificationSummarizationClient>(
            (sp, client) =>
            {
                var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<NotificationSummarizationOptions>>();
                client.BaseAddress = new Uri(options.Value.BaseUrl);
                client.DefaultRequestHeaders.Add("Accept", "text/event-stream");
            });

        return services;
    }
}
