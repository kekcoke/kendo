# Architecture Specification — Day 15

## Milestone Scope
- **Milestone:** M3.5 — Graceful shutdown
- **Roadmap phase:** 03 — High Availability & Chaos Testing
- **Components touched:**
  - `Kendo.Shared` — new `GracefulShutdown` module
  - `Gateway` — wire graceful shutdown config + drain middleware
  - `UserService` — wire graceful shutdown config + drain middleware
  - `Worker` — wire graceful shutdown config + drain middleware + update `WorkerBackgroundService`
  - `Kendo.Tests` — new test class for graceful shutdown behaviour
- **Explicitly out of scope:**
  - M3.6 — Ops Runbooks (documentation-only milestone, next after M3.5)
  - Phase 04 (not yet scoped)
  - Any changes to `docker-compose.yml`, `nginx.conf`, or CI pipeline

## Layer Changes

### Kendo.Shared
| Change | Details |
|---|---|
| **New file:** `GracefulShutdown/GracefulShutdownServiceCollectionExtensions.cs` | Extension method `AddKendoGracefulShutdown()` — configures `HostOptions.ShutdownTimeout` (default 30s, configurable via `GracefulShutdown__TimeoutSeconds`) |
| **New file:** `GracefulShutdown/GracefulShutdownMiddleware.cs` | Middleware that tracks in-flight requests via a shared counter; blocks new requests during drain phase (after `ApplicationStopping`) and waits for existing requests to complete within the shutdown timeout |

### Gateway
| Change | Details |
|---|---|
| `Program.cs` | Add `builder.Services.AddKendoGracefulShutdown(builder.Configuration)` |
| | Add `app.UseKendoGracefulShutdown()` in the middleware pipeline (after error handling, before routing) |

### UserService
| Change | Details |
|---|---|
| `Program.cs` | Add `builder.Services.AddKendoGracefulShutdown(builder.Configuration)` |
| | Add `app.UseKendoGracefulShutdown()` in the middleware pipeline |

### Worker
| Change | Details |
|---|---|
| `Program.cs` | Add `builder.Services.AddKendoGracefulShutdown(builder.Configuration)` |
| | Add `app.UseKendoGracefulShutdown()` in the middleware pipeline |
| `WorkerBackgroundService.cs` | No code changes needed — the existing `OperationCanceledException` catch + `stoppingToken` usage already handles graceful cancellation. The shutdown timeout from `HostOptions` ensures `ExecuteAsync` has time to complete its final iteration. |

### Kendo.Tests
| Change | Details |
|---|---|
| **New file:** `GracefulShutdown/GracefulShutdownMiddlewareTests.cs` | Unit tests verifying the drain counter behaviour |

## Data Contracts

### Configuration (`appsettings.json` / env vars)
```yaml
GracefulShutdown__TimeoutSeconds: 30  # optional, default 30s
```

No new API endpoints, message schemas, or DB migrations.

## Implementation Plan (Commit Units)

### Unit 1 — Graceful shutdown module in Kendo.Shared
- **Files:**
  - `src/Shared/GracefulShutdown/GracefulShutdownServiceCollectionExtensions.cs`
  - `src/Shared/GracefulShutdown/GracefulShutdownMiddleware.cs`
  - `src/Shared/GracefulShutdown/RequestTracker.cs`
- **Gate command:** `dotnet test --filter "Category=Unit" --no-restore`
- **Commit message:** `feat(shared): add graceful shutdown support with configurable drain timeout`

**Details:**

`RequestTracker.cs`:
```csharp
namespace Kendo.Shared.GracefulShutdown;

/// <summary>
/// Thread-safe counter tracking in-flight HTTP requests.
/// Used by GracefulShutdownMiddleware to block new requests during drain.
/// </summary>
public class RequestTracker
{
    private volatile int _inFlightCount;
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
```

`GracefulShutdownMiddleware.cs`:
```csharp
namespace Kendo.Shared.GracefulShutdown;

/// <summary>
/// Middleware that tracks in-flight requests and blocks new requests
/// during graceful shutdown (after IHostApplicationLifetime.ApplicationStopping).
/// Health check endpoints are always bypassed.
/// </summary>
public class GracefulShutdownMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RequestTracker _tracker;
    private static readonly PathString HealthLivePath = new("/health/live");
    private static readonly PathString HealthReadyPath = new("/health/ready");

    public GracefulShutdownMiddleware(RequestDelegate next, RequestTracker tracker)
    {
        _next = next;
        _tracker = tracker;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Bypass health endpoints — they must respond even during drain
        if (context.Request.Path == HealthLivePath ||
            context.Request.Path == HealthReadyPath)
        {
            await _next(context);
            return;
        }

        if (_tracker.IsDraining)
        {
            context.Response.StatusCode = 503;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsync(
                """{"type":"https://httpstatuses.com/503","title":"Service Unavailable","detail":"Server is shutting down — no new requests accepted","status":503}""");
            return;
        }

        using var scope = _tracker.BeginRequest();
        await _next(context);
    }
}
```

`GracefulShutdownServiceCollectionExtensions.cs`:
```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kendo.Shared.GracefulShutdown;

public static class GracefulShutdownServiceCollectionExtensions
{
    private const string TimeoutKey = "GracefulShutdown__TimeoutSeconds";

    public static IServiceCollection AddKendoGracefulShutdown(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var timeoutSeconds = configuration.GetValue<int?>(TimeoutKey) ?? 30;
        var timeout = TimeSpan.FromSeconds(timeoutSeconds);

        // Register singleton tracker
        services.AddSingleton<RequestTracker>();

        // Configure host shutdown timeout
        services.Configure<HostOptions>(options =>
        {
            options.ShutdownTimeout = timeout;
        });

        // Register middleware
        services.AddSingleton<GracefulShutdownMiddleware>();

        return services;
    }
}
```

---

### Unit 2 — Wire graceful shutdown into Gateway
- **Files:**
  - `src/Gateway/Program.cs`
- **Gate command:** `dotnet test --filter "Category=Unit" --no-restore`
- **Commit message:** `feat(gateway): wire graceful shutdown with drain middleware`

**Changes to `src/Gateway/Program.cs`:**
- Add `using Kendo.Shared.GracefulShutdown;`
- After `builder.Services.AddKendoRateLimiting(...)`: add `builder.Services.AddKendoGracefulShutdown(builder.Configuration);`
- After `app.UseKendoLoadShedding()`: add `app.UseMiddleware<GracefulShutdownMiddleware>();`

---

### Unit 3 — Wire graceful shutdown into UserService
- **Files:**
  - `src/UserService/Program.cs`
- **Gate command:** `dotnet test --filter "Category=Unit" --no-restore`
- **Commit message:** `feat(userservice): wire graceful shutdown with drain middleware`

**Changes to `src/UserService/Program.cs`:**
- Add `using Kendo.Shared.GracefulShutdown;`
- After `builder.Services.AddKendoErrorHandling()`: add `builder.Services.AddKendoGracefulShutdown(builder.Configuration);`
- After `app.UseKendoErrorHandling()`: add `app.UseMiddleware<GracefulShutdownMiddleware>();`

---

### Unit 4 — Wire graceful shutdown into Worker + test the drain
- **Files:**
  - `src/Worker/Program.cs`
- **Gate command:** `dotnet test --filter "Category=Unit" --no-restore`
- **Commit message:** `feat(worker): wire graceful shutdown with drain middleware`

**Changes to `src/Worker/Program.cs`:**
- Add `using Kendo.Shared.GracefulShutdown;`
- After `builder.Services.AddKendoErrorHandling()`: add `builder.Services.AddKendoGracefulShutdown(builder.Configuration);`
- After `app.UseKendoErrorHandling()`: add `app.UseMiddleware<GracefulShutdownMiddleware>();`

---

### Unit 5 — Graceful shutdown tests
- **Files:**
  - `tests/Kendo.Tests/GracefulShutdown/GracefulShutdownMiddlewareTests.cs`
- **Gate command:** `dotnet test --filter "Category=GracefulShutdown" --no-restore`
- **Commit message:** `test(shared): add graceful shutdown middleware and request tracker tests`

**Test coverage:**
| Test | Description |
|---|---|
| `RequestTracker_AllowsRequestWhenNotDraining` | BeginRequest succeeds, InFlightCount increments/decrements |
| `RequestTracker_BlocksRequestDuringDrain` | BeginRequest throws InvalidOperationException when draining |
| `RequestTracker_WaitForDrain_ReturnsTrueWhenCountZero` | WaitForDrain returns true immediately if no in-flight requests |
| `RequestTracker_WaitForDrain_BlocksUntilDrainComplete` | WaitForDrain blocks and returns true after requests complete within timeout |
| `RequestTracker_WaitForDrain_TimeoutReturnsFalse` | WaitForDrain returns false if drain doesn't complete in time |
| `Middleware_BypassesHealthEndpoints_WhenDraining` | Health endpoints return 200 even during drain |
| `Middleware_Returns503DuringDrain` | Non-health requests get 503 during drain |
| `Middleware_ProxiesNormalRequestsWhenNotDraining` | Normal requests pass through when not draining |
| `GracefulShutdownMiddleware_SetsCorrect503Body` | The 503 response is valid RFC 7807 JSON |

## Success Checklist
- [ ] All 3 services configure `HostOptions.ShutdownTimeout` to a configurable value (default 30s)
- [ ] In-flight HTTP requests are tracked via singleton counter in all 3 services
- [ ] During shutdown, new requests receive `503 Service Unavailable` with RFC 7807 body
- [ ] Health endpoints (`/health/live`, `/health/ready`) always respond — even during drain
- [ ] Existing HostedServices (`WorkerBackgroundService`, `OutboxRelayService`, `DlqDepthMonitor`) continue to use their `stoppingToken` as before — no change needed
- [ ] WaitForDrain blocks until all in-flight requests complete or timeout expires
- [ ] 9+ unit tests pass for graceful shutdown behaviour
- [ ] All existing Phase 01, 02, and Phase 03 (M3.1-M3.4) acceptance criteria still pass

## Resilience Mandate
- **Graceful shutdown itself:** The drain middleware is purely local (no external dependencies). The `RequestTracker` uses in-process volatile counters with `Interlocked` for thread safety. No circuit breaker or retry needed.
- **Health endpoints bypass:** Always functional during drain — ensures load balancer health probes continue to receive responses.
- **Service dependencies (DB, Redis, ASB):** Already protected by existing Polly circuit breaker + retry policies in `Kendo.Shared.Resilience`. The shutdown timeout ensures these dependencies are not force-killed mid-operation.
- **OutboxRelayService:** Already uses `stoppingToken` in its polling loop — will naturally stop on SIGTERM. The shutdown timeout gives it time to complete any in-flight message publish before process exit.
- **WorkerBackgroundService:** Already catches `OperationCanceledException` — no change needed.
- **DlqDepthMonitor:** Already passes `stoppingToken` to `Task.Delay` and the ASB admin client — will stop cleanly on SIGTERM.
- **N/A — no new external dependencies this milestone.**
