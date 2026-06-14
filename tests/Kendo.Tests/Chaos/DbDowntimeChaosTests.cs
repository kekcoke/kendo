using System.Net;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace Kendo.Tests.Chaos;

public class DbDowntimeChaosTests : ChaosTestBase
{
    public DbDowntimeChaosTests(ChaosTestFixture fixture, ITestOutputHelper output)
        : base(fixture, output) { }

    [Fact]
    public async Task PostgresPause_CircuitBreakerTrips_Returns503Fallback()
    {
        SkipIfDockerUnavailable();

        // Arrange: ensure system is healthy first
        var healthy = await Fixture.WaitForHealthAsync("/health/ready");
        Assert.True(healthy, "System must be healthy before chaos injection");

        // Act: pause PostgreSQL
        Output.WriteLine("Pausing PostgreSQL container...");
        var (exitCode, stdout, stderr) = await Fixture.RunDockerComposeAsync("pause postgres");
        Assert.Equal(0, exitCode);
        Output.WriteLine($"PostgreSQL paused. stdout: {stdout}");

        // Wait for circuit breaker to trip (Polly: 3 consecutive failures → open)
        await Task.Delay(TimeSpan.FromSeconds(8));

        // Assert: UserService returns 503 circuit breaker fallback
        var userServiceClient = new HttpClient { BaseAddress = new Uri(Fixture.UserServiceBaseUrl) };
        var userServiceResponse = await userServiceClient.GetAsync("/health/ready");

        Assert.True(
            ChaosTestFixture.IsCircuitBreakerFallback(userServiceResponse),
            $"Expected 503 circuit breaker fallback, got {userServiceResponse.StatusCode}"
        );
        Output.WriteLine("✓ UserService returns 503 fallback via circuit breaker");

        // Assert: Gateway still serves requests (no cascading failure)
        var gatewayResponse = await Fixture.HttpClient.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, gatewayResponse.StatusCode);
        Output.WriteLine("✓ Gateway continues serving requests (no cascade)");

        // Cleanup: unpause PostgreSQL
        Output.WriteLine("Unpausing PostgreSQL...");
        var (unpauseExitCode, _, _) = await Fixture.RunDockerComposeAsync("unpause postgres");
        Assert.Equal(0, unpauseExitCode);

        // Wait for recovery
        var recovered = await Fixture.WaitForHealthAsync("/health/ready", timeoutSeconds: 15);
        Assert.True(recovered, "System must recover after PostgreSQL unpause");
        Output.WriteLine("✓ System recovered after PostgreSQL unpause");
    }
}
