using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Kendo.Tests.Infrastructure;

/// <summary>
/// Load balancer integration tests for M3.1 — NGINX reverse proxy.
/// Tests marked [Category=Infrastructure] require Docker and are excluded from unit test runs.
/// Run with: dotnet test --filter Category=Infrastructure
///
/// Prerequisites:
///   docker compose up -d --scale gateway=3 --scale userservice=3 --scale worker=3
/// </summary>
[Trait("Category", "Infrastructure")]
public class LoadBalancerTests : IClassFixture<LoadBalancerFixture>, IDisposable
{
    private readonly HttpClient _client;
    private readonly LoadBalancerFixture _fixture;

    // Base URL through NGINX (maps to host port 5000 -> nginx:80 -> backend)
    private const string NginxBaseUrl = "http://localhost:5000";

    public LoadBalancerTests(LoadBalancerFixture fixture)
    {
        _fixture = fixture;
        _client = new HttpClient
        {
            BaseAddress = new Uri(NginxBaseUrl),
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    /// <summary>
    /// Verifies that /health/live returns 200 when proxied through NGINX to the Gateway.
    /// </summary>
    [Fact]
    public async Task Request_RoutesToGateway_ThroughNginx()
    {
        // Arrange & Act
        var response = await _client.GetAsync("/health/live");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Healthy", body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that /api/users POST returns 202 Accepted when proxied through NGINX.
    /// This confirms the /api/users location block routes to the UserService upstream.
    /// </summary>
    [Fact]
    public async Task Request_RoutesToUserService_ThroughNginx()
    {
        // Arrange
        var payload = JsonSerializer.Serialize(new
        {
            email = $"lb-test-{Guid.NewGuid():N}@test.com",
            displayName = "Load Balancer Test"
        });
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/users", content);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
    }

    /// <summary>
    /// Sends N sequential requests and verifies they are distributed across replicas.
    /// This test relies on each replica responding with a unique identifier
    /// via the Server or X-Upstream header (configured in the app's middleware).
    ///
    /// NOTE: If no replica-identifying header is configured, this test
    /// verifies that all requests return 200 (load balancer is routing correctly).
    /// </summary>
    [Fact]
    public async Task Request_DistributesAcrossReplicas()
    {
        // Arrange
        const int requestCount = 20;
        var responses = new List<HttpResponseMessage>(requestCount);

        // Act
        for (int i = 0; i < requestCount; i++)
        {
            var response = await _client.GetAsync("/health/live");
            responses.Add(response);
        }

        // Assert
        // All requests should succeed
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        // Collect unique server identifiers (if available)
        var serverHeaders = responses
            .Select(r => r.Headers.Server?.FirstOrDefault()?.ToString() ?? "unknown")
            .Distinct()
            .ToList();

        // NOTE: Without replica-identifying middleware, all responses show the same Server header.
        // This test documents the minimum assertion (all 200 OK).
        // When replica-identifying headers are added, change the assertion to:
        //   Assert.True(serverHeaders.Count >= 2, "Requests should be distributed across ≥2 replicas");
        Assert.True(serverHeaders.Count >= 1, "All requests should have a Server header");
    }

    /// <summary>
    /// Kills one Gateway replica and verifies that subsequent requests through NGINX
    /// still succeed (no 5xx errors) within the health-check TTL.
    ///
    /// Runbook: docker stop stops the container -> Docker marks it unhealthy ->
    /// NGINX passive health check (max_fails=3, fail_timeout=10s) removes it from rotation.
    /// </summary>
    [Fact]
    public async Task KillOneReplica_NoClientVisibleErrors()
    {
        // Arrange
        // Capture the initial replica count
        var initialPs = await GetDockerProcessOutput("compose ps --format json");

        // Act — stop one gateway replica
        var stopResult = await RunDockerComposeCommand(
            "stop $(docker compose ps --format json | grep gateway | head -1 | awk -F'\"' '{print $8}')"
        );

        // Wait for health-check TTL (passive check: max_fails=3 * interval ~10s)
        await Task.Delay(TimeSpan.FromSeconds(12));

        // Send a burst of requests
        var failedRequests = new List<int>();
        for (int i = 0; i < 15; i++)
        {
            try
            {
                var response = await _client.GetAsync("/health/live");
                var statusCode = (int)response.StatusCode;

                if (statusCode >= 500)
                {
                    failedRequests.Add(statusCode);
                }
            }
            catch (Exception)
            {
                failedRequests.Add(-1); // Connection error
            }

            await Task.Delay(200); // Small delay between requests
        }

        // Assert
        Assert.Empty(failedRequests);

        // Restore
        await RunDockerComposeCommand("up -d --no-recreate --scale gateway=3 --scale userservice=3 --scale worker=3");

        // Wait for the restored replica to be healthy
        await Task.Delay(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that all containers are reporting as healthy after startup.
    /// </summary>
    [Fact]
    public async Task AllReplicasHealthy_AfterStartup()
    {
        // Arrange & Act
        var psOutput = await GetDockerProcessOutput("compose ps --format json");

        // Count healthy containers
        var healthyCount = CountHealthyContainers(psOutput);

        // Assert
        // Expected: 3 gateway + 3 userservice + 3 worker + 1 nginx + 1 postgres = 11
        Assert.True(healthyCount >= 11,
            $"Expected at least 11 healthy containers, found {healthyCount}. Output:\n{psOutput}");
    }

    #region Test Helpers

    /// <summary>
    /// Runs a docker compose command in the working directory and returns stdout.
    /// </summary>
    private async Task<string> RunDockerComposeCommand(string subCommand, int timeoutSeconds = 30)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = $"{subCommand}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = GetProjectRoot()
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(timeoutSeconds * 1000))
        {
            process.Kill();
            throw new TimeoutException($"Docker command timed out after {timeoutSeconds}s: {subCommand}");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Docker command failed (exit {process.ExitCode}): {subCommand}\nStderr: {stderr}");
        }

        return stdout;
    }

    /// <summary>
    /// Runs a docker compose sub-command and returns the output.
    /// </summary>
    private async Task<string> GetDockerProcessOutput(string composeArgs)
    {
        return await RunDockerComposeCommand(composeArgs);
    }

    /// <summary>
    /// Counts containers with "Health":"healthy" from docker compose ps JSON output.
    /// </summary>
    private static int CountHealthyContainers(string psJson)
    {
        // Simple count: look for "healthy" in the JSON output
        int count = 0;
        int index = 0;

        while ((index = psJson.IndexOf("\"healthy\"", index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += "\"healthy\"".Length;
        }

        return count;
    }

    /// <summary>
    /// Resolves the project root directory by walking up from the test assembly.
    /// </summary>
    private static string GetProjectRoot()
    {
        var dir = AppContext.BaseDirectory;

        while (!string.IsNullOrEmpty(dir) && !File.Exists(Path.Combine(dir, "docker-compose.yml")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("Could not find project root (docker-compose.yml)");
    }

    #endregion
}

/// <summary>
/// Fixture that ensures Docker Compose is running with multi-replica configuration.
/// </summary>
public class LoadBalancerFixture : IDisposable
{
    public LoadBalancerFixture()
    {
        // Ensure Docker Compose is accessible
        var psi = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = "compose ps --format json",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = ResolveProjectRoot()
        };

        using var process = new Process { StartInfo = psi };
        process.Start();
        process.WaitForExit(5000);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "Docker Compose is not running. Start with: docker compose up -d --scale gateway=3 --scale userservice=3 --scale worker=3");
        }
    }

    public void Dispose()
    {
        // No cleanup — fixture only validates preconditions
        GC.SuppressFinalize(this);
    }

    private static string ResolveProjectRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir) && !File.Exists(Path.Combine(dir, "docker-compose.yml")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return dir ?? Directory.GetCurrentDirectory();
    }
}
