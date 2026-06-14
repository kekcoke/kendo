using System.Threading;

namespace Kendo.Shared.GracefulShutdown;

/// <summary>
/// Thread-safe counter tracking in-flight HTTP requests.
/// Used by GracefulShutdownMiddleware to block new requests during drain.
/// </summary>
public class RequestTracker
{
    private int _inFlightCount;
    private volatile bool _isDraining;
    private readonly object _lock = new();

    public int InFlightCount => _inFlightCount;

    public bool IsDraining => _isDraining;

    public IDisposable BeginRequest()
    {
        if (_isDraining)
            throw new InvalidOperationException("Server is shutting down");

        Interlocked.Increment(ref _inFlightCount);
        return new RequestScope(this);
    }

    public void EndRequest()
    {
        Interlocked.Decrement(ref _inFlightCount);
    }

    public void StartDraining()
    {
        lock (_lock)
        {
            _isDraining = true;
        }
    }

    /// <summary>
    /// Blocks until in-flight requests drain or timeout expires.
    /// Returns true if all requests completed; false if timeout elapsed.
    /// </summary>
    public bool WaitForDrain(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (_inFlightCount == 0)
                return true;
            Thread.Sleep(100);
        }
        return _inFlightCount == 0;
    }

    private sealed class RequestScope(RequestTracker tracker) : IDisposable
    {
        public void Dispose() => tracker.EndRequest();
    }
}
