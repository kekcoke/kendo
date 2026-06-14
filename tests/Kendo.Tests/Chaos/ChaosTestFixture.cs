using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Kendo.Tests.Chaos;

/// <summary>
/// Shared infrastructure for chaos tests: Docker CLI wrapper, HTTP client factory,
/// health-assertion utilities, and logging.
///
/// All chaos tests require Docker to be running (Docker Desktop / Docker Engine).
/// Tests are decorated with [Trait("Category", "Chaos")] and skipped automatically
/// when Docker is not available.
/// </summary>
public sealed class ChaosTestFixture : IAsyncLifetime
{
    private bool _dockerAvailable;

    public ILogger Logger { get; private set; } = null!;
    public HttpClient HttpClient { get; private set; } = null!;
    public string GatewayBaseUrl { get; } = "http://localhost:5000";
    public string UserServiceBaseUrl { get; } = "http://localhost:5001";
    public string WorkerBaseUrl { get; } = "http://localhost:5002";

    public ChaosTestFixture()
    {
        HttpClient = new HttpClient
        {
            BaseAddress = new Uri(GatewayBaseUrl),
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    public async Task InitializeAsync()
    {
        _dockerAvailable = await CheckDockerAvailableAsync();
        if (!_dockerAvailable)
        {
            Logger.LogWarning("Docker not available — chaos tests will be skipped");
        }
    }

    public Task DisposeAsync()
    {
        HttpClient.Dispose();
        return Task.CompletedTask;
    }

    public bool IsDockerAvailable => _dockerAvailable;

    /// <summary>
    /// Returns true if the Docker CLI is reachable and docker compose is installed.
    /// </summary>
    private static async Task<bool> CheckDockerAvailableAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "info --format '{{.ServerVersion}}'",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process == null) return false;

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Execute a docker compose command and return (exitCode, stdout, stderr).
    /// </summary>
    public async Task<(int ExitCode, string Stdout, string Stderr)> RunDockerComposeAsync(
        string arguments, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = $"compose {arguments}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            return (-1, string.Empty, "Failed to start docker compose process");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync(ct);

        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    /// <summary>
    /// Wait for a service to become healthy by polling its health endpoint.
    /// Returns true if healthy within the timeout.
    /// </summary>
    public async Task<bool> WaitForHealthAsync(string url, int timeoutSeconds = 30)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                var response = await HttpClient.GetAsync(url, cts.Token);
                if (response.IsSuccessStatusCode)
                    return true;
            }
            catch
            {
                // Service not ready yet
            }
            await Task.Delay(1000, cts.Token);
        }
        return false;
    }

    /// <summary>
    /// Verify the response indicates a circuit-breaker fallback (503 + ProblemDetails).
    /// </summary>
    public static bool IsCircuitBreakerFallback(HttpResponseMessage response)
    {
        return (int)response.StatusCode == 503
            && response.Content.Headers.ContentType?.MediaType == "application/problem+json";
    }
}
