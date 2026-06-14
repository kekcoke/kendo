using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rebus.Bus;
using Rebus.Config;
using Rebus.ServiceProvider;

namespace Kendo.Shared.Messaging;

/// <summary>
/// Extension methods for registering a Rebus consumer on the Azure Service Bus
/// Dead Letter Queue (kendo-events/$DeadLetterQueue).
/// 
/// The DLQ consumer is disabled by default and must be opted in via
/// configuration (Rebus__DlqConsumerEnabled = true) to prevent accidental
/// DLQ consumption in local development environments.
/// </summary>
public static class KendoRebusDlqConfiguration
{
    private const string ConnectionStringKey = "Rebus__ConnectionString";
    private const string DlqQueueName = "kendo-events/$DeadLetterQueue";
    private const string DlqEnabledKey = "Rebus__DlqConsumerEnabled";

    /// <summary>
    /// Registers a Rebus consumer that listens on the Azure Service Bus Dead Letter Queue.
    /// </summary>
    /// <remarks>
    /// - Requires <c>Rebus__ConnectionString</c> to be set (same as main Rebus config).
    /// - Requires <c>Rebus__DlqConsumerEnabled</c> = <c>true</c> to activate.
    /// - Uses 1 worker (DLQ is a low-volume operational queue; no parallelism needed).
    /// - Gracefully skips registration if connection string is missing or DLQ is disabled.
    /// </remarks>
    public static IServiceCollection AddKendoRebusDlqConsumer(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration[ConnectionStringKey];
        var dlqEnabled = configuration.GetValue<bool>(DlqEnabledKey);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Graceful skip — local dev without ASB
            return services;
        }

        if (!dlqEnabled)
        {
            // DLQ consumer is opt-in — skip by default
            return services;
        }

        // Register handlers from the assembly that contains DeadLetteredMessage
        services.AutoRegisterHandlersFromAssemblyOf<DeadLetteredMessage>();

        services.AddRebus(
            configure => configure
                .Transport(t => t.UseAzureServiceBus(connectionString, DlqQueueName))
                .Options(o =>
                {
                    o.SetNumberOfWorkers(1);
                    o.SetMaxParallelism(1);
                }),
            onCreated: async bus =>
            {
                // DLQ consumer started — ready to receive dead-lettered messages
                await Task.CompletedTask;
            });

        return services;
    }
}
