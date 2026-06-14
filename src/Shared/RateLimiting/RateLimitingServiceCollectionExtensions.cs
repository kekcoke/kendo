using System.Threading.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Kendo.Shared.RateLimiting;

public static class RateLimitingServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="FixedWindowRateLimiter"/> and <see cref="ConcurrencyLimiter"/>
    /// as singletons, bound to configuration. The limiters are injected directly into
    /// middleware (RateLimitingMiddleware, LoadSheddingMiddleware).
    /// </summary>
    public static IServiceCollection AddKendoRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<RateLimitingOptions>(
            configuration.GetSection(RateLimitingOptions.SectionName));

        // Register FixedWindowRateLimiter as a singleton
        services.AddSingleton<FixedWindowRateLimiter>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<RateLimitingOptions>>().Value.FixedWindow;

            return new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
            {
                PermitLimit = opts.PermitLimit,
                Window = TimeSpan.FromSeconds(opts.WindowSeconds),
                QueueProcessingOrder = Enum.Parse<QueueProcessingOrder>(opts.QueueProcessingOrder),
                QueueLimit = opts.QueueLimit,
                AutoReplenishment = true
            });
        });

        // Register ConcurrencyLimiter as a singleton
        services.AddSingleton<ConcurrencyLimiter>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<RateLimitingOptions>>().Value.Concurrency;

            return new ConcurrencyLimiter(new ConcurrencyLimiterOptions
            {
                PermitLimit = opts.MaxConcurrency,
                QueueProcessingOrder = Enum.Parse<QueueProcessingOrder>(opts.QueueProcessingOrder),
                QueueLimit = opts.QueueLimit
            });
        });

        return services;
    }
}
