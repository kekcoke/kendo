using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rebus.Bus;
using Rebus.Config;
using Rebus.ServiceProvider;

namespace Kendo.Shared.Messaging;

/// <summary>
/// Extension methods for registering a Rebus producer (one-way client) targeting
/// the dedicated AI flow queue (<c>kendo-events-ai</c>). Used by the Gateway and
/// UserService to publish AI-domain events without consuming from the queue.
///
/// Gracefully skips registration when the connection string is empty/missing,
/// enabling local development without Azure Service Bus.
/// </summary>
public static class KendoRebusAiProducerConfiguration
{
    private const string ConnectionStringKey = "Rebus__ConnectionString";

    /// <summary>
    /// Registers a one-way Rebus client that publishes to <c>kendo-events-ai</c>.
    /// </summary>
    public static IServiceCollection AddKendoRebusAiProducer(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration[ConnectionStringKey];

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Graceful skip — local dev without ASB
            return services;
        }

        services.AddRebus(
            configure => configure
                .Transport(t => t.UseAzureServiceBusAsOneWayClient(connectionString))
                .Options(o =>
                {
                    o.SetMaxParallelism(1);
                }));

        return services;
    }
}
