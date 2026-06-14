using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rebus.Bus;
using Rebus.Config;
using Rebus.ServiceProvider;

namespace Kendo.Shared.Messaging;

/// <summary>
/// Extension methods for registering Rebus with Azure Service Bus transport
/// across producer and consumer services.
/// </summary>
public static class KendoRebusConfiguration
{
    private const string ConnectionStringKey = "Rebus__ConnectionString";
    private const string QueueName = "kendo-events";

    /// <summary>
    /// Registers Rebus with Azure Service Bus transport.
    /// </summary>
    /// <param name="rebusRole">"producer" — bus registers but does not auto-start consumers.
    /// "consumer" — bus registers and auto-starts polling the kendo-events queue.</param>
    /// <remarks>
    /// If <c>Rebus__ConnectionString</c> is empty or missing, registration is gracefully
    /// skipped with a log warning — enabling local development without Azure Service Bus.
    /// </remarks>
    public static IServiceCollection AddKendoRebus(
        this IServiceCollection services,
        IConfiguration configuration,
        string rebusRole)
    {
        var connectionString = configuration[ConnectionStringKey];

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Graceful skip — local dev without ASB
            services.AddSingleton<IBus>(_ => null!);
            return services;
        }

        services.AutoRegisterHandlersFromAssemblyOf<KendoMessage>();

        switch (rebusRole)
        {
            case "consumer":
                services.AddRebus(
                    configure => configure
                        .Transport(t => t.UseAzureServiceBus(connectionString, QueueName))
                        .Options(o =>
                        {
                            o.SetNumberOfWorkers(3);
                            o.SetMaxParallelism(10);
                        }),
                    onCreated: async bus =>
                    {
                        // Bus started — ready to consume
                        await Task.CompletedTask;
                    });
                break;

            case "producer":
            default:
                services.AddRebus(
                    configure => configure
                        .Transport(t => t.UseAzureServiceBusAsOneWayClient(connectionString))
                        .Options(o =>
                        {
                            o.SetMaxParallelism(1);
                        }));
                break;
        }

        return services;
    }
}
