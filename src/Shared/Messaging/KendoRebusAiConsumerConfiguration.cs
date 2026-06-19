using Kendo.Shared.Messaging.Events;
using Kendo.Shared.Messaging.Topology;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rebus.Bus;
using Rebus.Config;
using Rebus.Routing.TypeBased;
using Rebus.ServiceProvider;

namespace Kendo.Shared.Messaging;

/// <summary>
/// Extension methods for registering a Rebus consumer on the dedicated AI flow
/// queue (<c>kendo-events-ai</c>). This consumer operates independently from the
/// user-lifecycle consumer on <c>kendo-events</c> per CCD-4 isolation guarantees.
///
/// Configuration (via appsettings.json or env vars):
/// - <c>Rebus__ConnectionString</c>: ASB connection string (same as main Rebus config)
/// - <c>Rebus__AiWorkers</c>: number of worker threads (default: 2)
/// - <c>Rebus__AiParallelism</c>: max parallelism (default: 5)
///
/// Gracefully skips registration when the connection string is empty/missing,
/// enabling local development without Azure Service Bus.
/// </summary>
public static class KendoRebusAiConsumerConfiguration
{
    private const string ConnectionStringKey = "Rebus__ConnectionString";

    /// <summary>
    /// Registers a Rebus consumer on the <c>kendo-events-ai</c> queue with
    /// routing for the 4 AI event types.
    /// </summary>
    public static IServiceCollection AddKendoRebusAiConsumer(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration[ConnectionStringKey];

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Graceful skip — local dev without ASB
            return services;
        }

        var workerCount = configuration.GetValue<int>("Rebus__AiWorkers");
        if (workerCount <= 0) workerCount = 2;

        var maxParallelism = configuration.GetValue<int>("Rebus__AiParallelism");
        if (maxParallelism <= 0) maxParallelism = 5;

        services.AutoRegisterHandlersFromAssemblyOf<EventIngestedEvent>();

        services.AddRebus(
            configure => configure
                .Transport(t => t.UseAzureServiceBus(connectionString, KendoTopology.AiFlowQueue))
                .Options(o =>
                {
                    o.SetNumberOfWorkers(workerCount);
                    o.SetMaxParallelism(maxParallelism);
                })
                .Routing(r => r.TypeBased()
                    .Map<EventIngestedEvent>(KendoTopology.AiFlowQueue)
                    .Map<EventValidatedEvent>(KendoTopology.AiFlowQueue)
                    .Map<UserEmbeddingUpdatedEvent>(KendoTopology.AiFlowQueue)
                    .Map<NotificationRequestedEvent>(KendoTopology.AiFlowQueue)));

        return services;
    }
}
