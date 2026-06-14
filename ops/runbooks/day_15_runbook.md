# Day 15 Runbook — Graceful Shutdown (M3.5)
> **Phase 4 artifact.** Created by DevOps/SRE agent.

## Overview
All 3 services (Gateway, UserService, Worker) now support graceful shutdown:
- **SIGTERM handler** via ASP.NET Core's built-in `IHostApplicationLifetime`
- **Drain middleware** blocks new requests during shutdown (503 RFC 7807)
- **Configurable drain timeout** via `GracefulShutdown__TimeoutSeconds` (default: 30s)
- **Health endpoints bypass** — `/health/live` and `/health/ready` always respond 200

## Architecture

```
Process receives SIGTERM
  └─ ASP.NET Core Host begins shutdown
       └─ GracefulShutdownHostedService.StopAsync()
            ├─ RequestTracker.StartDraining() → middleware rejects new requests (503)
            └─ RequestTracker.WaitForDrain(timeout) → blocks until in-flight requests complete
                 └─ After drain/timeout → HostedServices are stopped → process exits
```

## Configuration

| Parameter | Default | Description |
|---|---|---|
| `GracefulShutdown__TimeoutSeconds` | 30 | Max time (seconds) to wait for in-flight requests to complete before hard shutdown |

Set via environment variable or `appsettings.json`:
```json
{
  "GracefulShutdown__TimeoutSeconds": 30
}
```

## Middleware Execution Order

### Gateway
```
Request → GracefulShutdownMiddleware → LoadSheddingMiddleware → RateLimitingMiddleware → ProblemDetailsMiddleware → Controllers
         (503 if draining)             (503 if saturated)       (429 if exceeded)        (RFC 7807 body)
```

### UserService
```
Request → GracefulShutdownMiddleware → ProblemDetailsMiddleware → Controllers
         (503 if draining)
```

### Worker
```
Request → GracefulShutdownMiddleware → ProblemDetailsMiddleware → Controllers
         (503 if draining)
```

## Failure Scenarios & Recovery

### 1. Server shutting down (503)
- **Symptom:** Client receives `503 Service Unavailable` with RFC 7807 body `"Server is shutting down — no new requests accepted"`
- **Cause:** Process received SIGTERM (e.g., Kubernetes pod termination, `docker compose down`, manual kill)
- **Resolution:** Client retries with exponential backoff — traffic is automatically routed to healthy replicas by the load balancer
- **Monitoring:** Middleware logs incoming requests during drain at `Warning` level

### 2. Drain timeout expired
- **Symptom:** In-flight requests are aborted after `GracefulShutdown__TimeoutSeconds`
- **Cause:** Long-running request (e.g., slow DB query, downstream timeout) exceeds the drain window
- **Resolution:**
  - Increase `GracefulShutdown__TimeoutSeconds` if the service normally handles slow operations
  - Reduce upstream dependency timeouts to ensure requests complete within the drain window
  - Check for connection leaks or stuck operations

### 3. Health check failure during shutdown
- **Both middleware bypass** `/health/live` and `/health/ready` — health probes always respond even during drain
- If a health check fails during shutdown, the load balancer will see the container as healthy until it actually exits
- This is **by design** — the drain window allows the load balancer to detect the shutdown and stop routing new traffic

## Verification Commands

```bash
# Verify health endpoints work during drain simulation
# (Requires manual SIGTERM — e.g., docker stop with timeout)
docker compose up -d
CONTAINER=$(docker compose ps -q gateway)
docker stop -t 30 "$CONTAINER" &
sleep 1
# During drain window, health endpoints should still respond
curl -s -o /dev/null -w "Health: %{http_code}\n" http://localhost:5000/health/live
# Non-health requests should return 503 during drain
curl -s -o /dev/null -w "API: %{http_code}\n" http://localhost:5000/api/users
wait

# Verify drain timeout via env var
GracefulShutdown__TimeoutSeconds=5 docker compose up -d
```

## Docker Compose Integration
Docker Compose's default `stop_grace_period` is 10s. The `GracefulShutdown__TimeoutSeconds` default of 30s exceeds this, meaning Docker will send SIGKILL after 10s. **To use the full 30s drain window**, set `stop_grace_period` in `docker-compose.yml`:

```yaml
services:
  gateway:
    stop_grace_period: 35s
  userservice:
    stop_grace_period: 35s
  worker:
    stop_grace_period: 35s
```

**Alternatively**, set `GracefulShutdown__TimeoutSeconds` to a value below 10s (default `stop_grace_period`).

## Existing HostedService Behaviour

| Service | Existing Behaviour | Graceful Shutdown Impact |
|---|---|---|
| `WorkerBackgroundService` | Catches `OperationCanceledException` | ✅ No change — stops cleanly on `stoppingToken` |
| `OutboxRelayService` (UserService) | Uses `stoppingToken` in polling loop | ✅ No change — completes current iteration before exit |
| `DlqDepthMonitor` (Worker) | Passes `stoppingToken` to `Task.Delay` and ASB admin client | ✅ No change — stops cleanly |

## Rollback Plan
1. Revert the feature branch merge from `develop`
2. Remove `using Kendo.Shared.GracefulShutdown;` and `app.UseMiddleware<GracefulShutdownMiddleware>();` from all 3 `Program.cs` files
3. Remove `builder.Services.AddKendoGracefulShutdown()` from all 3 `Program.cs` files
4. Delete `src/Shared/GracefulShutdown/` directory
5. Delete `tests/Kendo.Tests/GracefulShutdown/` directory
6. Remove `stop_grace_period` overrides from `docker-compose.yml` (if added)
