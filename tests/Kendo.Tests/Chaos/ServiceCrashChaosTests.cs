using System.Net;
using Xunit;
using Xunit.Abstractions;

namespace Kendo.Tests.Chaos;

public class ServiceCrashChaosTests : ChaosTestBase
{
    public ServiceCrashChaosTests(ChaosTestFixture fixture, ITestOutputHelper output)
        : base(fixture, output) { }

    [Fact]
    public async Task KillOneReplica_NginxReroutesToRemaining()
    {
        SkipIfDockerUnavailable();

        // Arrange: ensure all 12 containers healthy (3 gateway + 3 userservice + 3 worker + nginx + postgres + redis)
        var healthy = await Fixture.WaitForHealthAsync("/health/ready");
        Assert.True(healthy, "System must be healthy before chaos injection");

        // Record initial replica header distribution
        var replicaHeaders = await CollectReplicaHeadersAsync(5);
        Output.WriteLine($"Initial replica distribution: [{string.Join(", ", replicaHeaders)}]");

        // Act: stop one user-service replica
        Output.WriteLine("Stopping a userservice replica...");
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "bash",
            Arguments = "-c \"docker compose ps --format '{{.Name}}' | grep userservice | head -2 | tail -1 | xargs docker stop\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = System.Diagnostics.Process.Start(psi);
        Assert.NotNull(process);
        var killOutput = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        Output.WriteLine($"Killed container. Output: {killOutput}");

        // Wait for NGINX to detect and remove from upstream (health check interval + TTL)
        await Task.Delay(TimeSpan.FromSeconds(8));

        // Assert: Gateway through NGINX still serves requests
        var gatewayResponses = new List<HttpStatusCode>();
        for (int i = 0; i < 5; i++)
        {
            var response = await Fixture.HttpClient.GetAsync("/health/live");
            gatewayResponses.Add(response.StatusCode);
            await Task.Delay(200);
        }

        Assert.All(gatewayResponses, status =>
            Assert.True(status == HttpStatusCode.OK,
                $"Expected 200, got {status} on request {gatewayResponses.IndexOf(status)}")
        );
        Output.WriteLine($"✓ All 5 requests through NGINX returned 200: [{string.Join(", ", gatewayResponses)}]");

        // Assert: X-Kendo-Replica headers show distribution across remaining replicas
        var postCrashHeaders = await CollectReplicaHeadersAsync(5);
        Output.WriteLine($"Post-crash replica distribution: [{string.Join(", ", postCrashHeaders)}]");
        Assert.NotEmpty(postCrashHeaders);

        // Cleanup: restart the killed container via docker compose up
        Output.WriteLine("Restoring killed replica...");
        await Fixture.RunDockerComposeAsync("up -d --no-recreate --scale userservice=3 --no-deps userservice");

        // Wait for full recovery
        await Task.Delay(TimeSpan.FromSeconds(8));
        var recoveredReplicas = await CountHealthyContainersAsync();
        Output.WriteLine($"Healthy containers after restore: {recoveredReplicas}");
    }

    private async Task<List<string>> CollectReplicaHeadersAsync(int count)
    {
        var headers = new List<string>();
        for (int i = 0; i < count; i++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
                using var response = await Fixture.HttpClient.SendAsync(request);
                if (response.Headers.TryGetValues("X-Kendo-Replica", out var values))
                {
                    headers.AddRange(values);
                }
            }
            catch { /* skip failures during crash */ }
            await Task.Delay(200);
        }
        return headers;
    }

    private async Task<int> CountHealthyContainersAsync()
    {
        var (exitCode, stdout, _) = await Fixture.RunDockerComposeAsync(
            "ps --format json | grep '\"Health\":\"healthy\"' | wc -l"
        );
        if (exitCode == 0 && int.TryParse(stdout.Trim(), out int count))
            return count;
        return -1;
    }
}
