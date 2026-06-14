using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kendo.Shared.Caching;

public static class DistributedCacheServiceCollectionExtensions
{
    public static IServiceCollection AddKendoDistributedCache(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration["Redis__ConnectionString"]
                               ?? configuration["RedisConnectionStrings__DefaultConnection"];

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = connectionString;
                options.InstanceName = "kendo:";
            });
        }
        else
        {
            // Fallback: in-memory cache when Redis is not configured (local dev / CI)
            services.AddDistributedMemoryCache();
        }

        return services;
    }
}
