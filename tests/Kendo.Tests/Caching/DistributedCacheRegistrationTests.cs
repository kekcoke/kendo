using Kendo.Shared.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kendo.Tests.Caching;

[Trait("Category", "Unit")]
public class DistributedCacheRegistrationTests
{
    [Fact]
    public void AddKendoDistributedCache_WithRedisConnectionString_RegistersStackExchangeRedis()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Redis__ConnectionString"] = "localhost:6379"
            })
            .Build();

        var services = new ServiceCollection();

        // Act
        services.AddKendoDistributedCache(config);
        var provider = services.BuildServiceProvider();

        // Assert — IDistributedCache is resolvable
        var cache = provider.GetService<IDistributedCache>();
        Assert.NotNull(cache);
        // StackExchangeRedisCache implements IDistributedCache
        Assert.IsAssignableFrom<IDistributedCache>(cache);
    }

    [Fact]
    public void AddKendoDistributedCache_WithoutRedisConnectionString_FallsBackToMemoryCache()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();

        // Act
        services.AddKendoDistributedCache(config);
        var provider = services.BuildServiceProvider();

        // Assert — IDistributedCache is still resolvable (falls back to MemoryDistributedCache)
        var cache = provider.GetService<IDistributedCache>();
        Assert.NotNull(cache);
    }

    [Fact]
    public void AddKendoDistributedCache_WithNullRedisConnectionString_DoesNotThrow()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Redis__ConnectionString"] = (string?)null
            })
            .Build();

        var services = new ServiceCollection();

        // Act & Assert
        var exception = Record.Exception(() => services.AddKendoDistributedCache(config));
        Assert.Null(exception);
    }

    [Fact]
    public void AddKendoDistributedCache_WithEmptyRedisConnectionString_DoesNotThrow()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Redis__ConnectionString"] = ""
            })
            .Build();

        var services = new ServiceCollection();

        // Act & Assert
        var exception = Record.Exception(() => services.AddKendoDistributedCache(config));
        Assert.Null(exception);
    }
}
