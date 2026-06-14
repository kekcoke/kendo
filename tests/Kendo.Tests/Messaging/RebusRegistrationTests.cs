using Kendo.Shared.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rebus.Bus;

namespace Kendo.Tests.Messaging;

[Trait("Category", "Messaging")]
public class RebusRegistrationTests
{
    private static IConfiguration CreateConfig(string? connectionString = null)
    {
        var data = new Dictionary<string, string?>();
        if (connectionString is not null)
            data["Rebus__ConnectionString"] = connectionString;

        return new ConfigurationBuilder()
            .AddInMemoryCollection(data)
            .Build();
    }

    [Fact]
    public void Producer_Registration_ResolvesIBus()
    {
        var config = CreateConfig("Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=test");
        var services = new ServiceCollection();
        services.AddSingleton(config);
        services.AddKendoRebus(config, "producer");
        var provider = services.BuildServiceProvider();

        var bus = provider.GetService<IBus>();

        Assert.NotNull(bus);
    }

    [Fact]
    public void Consumer_Registration_RegistersWithoutThrowing()
    {
        var config = CreateConfig("Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=test");
        var services = new ServiceCollection();
        services.AddSingleton(config);

        // Should not throw during registration itself
        var ex = Record.Exception(() => services.AddKendoRebus(config, "consumer"));

        Assert.Null(ex);

        // Verify the IBus registration descriptor exists in the service collection
        var registrations = services.Where(sd => sd.ServiceType == typeof(IBus)).ToList();

        Assert.NotEmpty(registrations);
    }

    [Fact]
    public void MissingConnectionString_GracefullySkips()
    {
        var config = CreateConfig(connectionString: null);
        var services = new ServiceCollection();
        services.AddSingleton(config);
        services.AddKendoRebus(config, "producer");
        var provider = services.BuildServiceProvider();

        // Should not throw — bus resolves to null
        var bus = provider.GetService<IBus>();

        Assert.Null(bus);
    }

    [Fact]
    public void EmptyConnectionString_GracefullySkips()
    {
        var config = CreateConfig(connectionString: "");
        var services = new ServiceCollection();
        services.AddSingleton(config);
        services.AddKendoRebus(config, "consumer");
        var provider = services.BuildServiceProvider();

        var bus = provider.GetService<IBus>();

        Assert.Null(bus);
    }
}
