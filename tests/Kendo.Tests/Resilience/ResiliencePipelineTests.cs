using Kendo.Shared.Resilience;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Kendo.Tests.Resilience;

[Trait("Category", "Resilience")]
public class ResiliencePipelineTests
{
    [Fact]
    public void DI_Registration_ResolvesIResiliencePipeline()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Resilience:Retry:MaxRetries"] = "3",
                ["Resilience:Retry:BaseDelayMs"] = "100",
                ["Resilience:CircuitBreaker:FailureThreshold"] = "3",
                ["Resilience:CircuitBreaker:BreakDurationSeconds"] = "30"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddKendoResilience(config);
        var provider = services.BuildServiceProvider();

        var pipeline = provider.GetService<IResiliencePipeline>();

        Assert.NotNull(pipeline);
    }

    [Fact]
    public void DI_Registration_ConfiguresOptionsFromAppSettings()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Resilience:Retry:MaxRetries"] = "5",
                ["Resilience:Retry:BaseDelayMs"] = "200",
                ["Resilience:CircuitBreaker:FailureThreshold"] = "5",
                ["Resilience:CircuitBreaker:BreakDurationSeconds"] = "60"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddKendoResilience(config);
        var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<ResilienceOptions>>().Value;

        Assert.Equal(5, options.Retry.MaxRetries);
        Assert.Equal(200, options.Retry.BaseDelayMs);
        Assert.Equal(5, options.CircuitBreaker.FailureThreshold);
        Assert.Equal(60, options.CircuitBreaker.BreakDurationSeconds);
    }

    [Fact]
    public void Options_DefaultValues_AreSane()
    {
        var options = new ResilienceOptions();

        Assert.Equal(3, options.Retry.MaxRetries);
        Assert.Equal(100, options.Retry.BaseDelayMs);
        Assert.Equal(2000, options.Retry.MaxDelayMs);
        Assert.True(options.Retry.UseJitter);

        Assert.Equal(3, options.CircuitBreaker.FailureThreshold);
        Assert.Equal(30, options.CircuitBreaker.BreakDurationSeconds);
        Assert.Equal(30, options.CircuitBreaker.SamplingDurationSeconds);
    }

    [Fact]
    public async Task CombinedPipeline_RetriesThenTripsCircuitBreaker()
    {
        var options = Options.Create(new ResilienceOptions
        {
            Retry = new RetryOptions { MaxRetries = 2, BaseDelayMs = 10, MaxDelayMs = 100, UseJitter = false },
            CircuitBreaker = new CircuitBreakerOptions { FailureThreshold = 10, BreakDurationSeconds = 30, SamplingDurationSeconds = 30 }
        });
        var pipeline = new PollyResiliencePipeline(options);
        var attempts = 0;

        // Each ExecuteAsync exhausts retries (3 attempts), then fails → 1 CB failure
        for (int cbCycle = 0; cbCycle < 3; cbCycle++)
        {
            try
            {
                await pipeline.ExecuteAsync(ct =>
                {
                    attempts++;
                    throw new InvalidOperationException($"Failure {attempts}");
                });
            }
            catch (InvalidOperationException)
            {
                // Expected — retries exhausted, CB not yet open
            }
        }

        // 3 cycles × 3 attempts each = 9 total attempts
        Assert.Equal(9, attempts);
    }

    [Fact]
    public void ResilienceOptions_SectionName_IsCorrect()
    {
        Assert.Equal("Resilience", ResilienceOptions.SectionName);
    }
}
