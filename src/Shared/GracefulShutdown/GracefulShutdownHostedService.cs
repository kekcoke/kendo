using Microsoft.Extensions.Hosting;

namespace Kendo.Shared.GracefulShutdown;

/// <summary>
/// Hosted service that triggers drain mode on graceful shutdown.
/// On StopAsync, marks the RequestTracker as draining and waits
/// for in-flight requests to complete within the shutdown timeout.
/// Registered automatically by AddKendoGracefulShutdown().
/// </summary>
public class GracefulShutdownHostedService : IHostedService
{
    private readonly RequestTracker _tracker;
    private readonly TimeSpan _drainTimeout;

    public GracefulShutdownHostedService(RequestTracker tracker, TimeSpan drainTimeout)
    {
        _tracker = tracker;
        _drainTimeout = drainTimeout;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // No-op — tracking starts on first HTTP request via middleware
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Signal middleware to reject new requests
        _tracker.StartDraining();

        // Wait for in-flight requests to drain within the configured timeout
        if (_tracker.InFlightCount > 0)
        {
            _tracker.WaitForDrain(_drainTimeout);
        }
    }
}
