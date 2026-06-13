# Ops Runbook — Day 4

> **Milestone:** M1.4 — Observability foundation  
> **Services:** Gateway (port 5000), UserService (port 5001), Worker (port 5002)  
> **Date:** 2026-06-13

---

## Health & Observability Contract

| Service | Liveness Probe | Readiness Probe | Expected Response |
|---------|---------------|-----------------|-------------------|
| Gateway | `GET :5000/health/live` | `GET :5000/health/ready` | `200 OK` — `Healthy` / `Ready` |
| UserService | `GET :5001/health/live` | `GET :5001/health/ready` | `200 OK` — `Healthy` / `Ready` |
| Worker | `GET :5002/health/live` | `GET :5002/health/ready` | `200 OK` — `Healthy` / `Ready` |

**OpenTelemetry console exporter:** All services emit structured traces and spans to stdout.  
**Trace correlation:** ASP.NET Core auto-correlation via `Activity.Current.TraceId` — trace IDs appear in all `ILogger` log lines.

---

## What Changed This Day

| Change | Impact |
|---|---|
| OpenTelemetry NuGet packages added to all services | Trace spans emitted to stdout via console exporter |
| `AddKendoObservability()` extension method in `Kendo.Shared` | Centralized OTel configuration — one call per service |
| Polly callbacks upgraded from placeholders to structured `ILogger` calls | Retry/CB state transitions visible in logs with trace correlation |
| PostgreSQL service container added to CI `build-and-test` job | Data integration tests now pass in CI without manual DB provisioning |

---

## Verification Playbook

### 1. Verify OpenTelemetry Traces

```bash
# Start all services
docker compose up -d

# Check a service's logs for trace spans
docker compose logs gateway | grep "ActivityStarted\|Span"

# Expected: Console exporter should show span data for health check requests
```

### 2. Verify Trace Correlation in Resilience Logging

```bash
# Trigger a user creation request
curl -X POST http://localhost:5000/users -H "Content-Type: application/json" -d '{"name":"test"}'

# Check UserService logs for trace ID presence
docker compose logs userservice | grep "TraceId"

# Expected: Each log line should contain TraceId matching the request's trace
```

### 3. Verify Circuit Breaker Logging

```bash
# Simulate DB failure (stop PostgreSQL)
docker compose stop postgres

# Trigger requests to UserService
for i in $(seq 1 10); do
  curl http://localhost:5001/users 2>&1
  sleep 0.5
done

# Check logs for circuit breaker state transitions
docker compose logs userservice | grep -i "circuit breaker"

# Expected output:
#   [Warning] Retry attempt 1/3 after 100ms — NpgsqlException: connection refused
#   [Error] Circuit breaker OPENED — 3 failures in 30s
#   [Information] Circuit breaker HALF-OPENED — probe request allowed
#   [Information] Circuit breaker CLOSED — normal operation resumed

# Restart PostgreSQL
docker compose start postgres
```

### 4. Verify CI Pipeline

```bash
# The CI pipeline now runs data integration tests against a PostgreSQL service container.
# Check the latest run on the feature branch:
#   https://github.com/kekcoke/kendo/actions
```

---

## Rollback Plan

### Rollback Strategy
1. **Revert feature branch:** Close PR without merging.
2. **If already merged:** `git revert` the merge commit on `develop`.
3. **Verify:** Run `docker compose up -d` and confirm all health endpoints respond.

### Rollback Scope
| Component | Rollback Action | Impact |
|---|---|---|
| OpenTelemetry packages | Revert `Kendo.Shared.csproj` changes | No traces emitted — back to M1.3 state |
| `AddKendoObservability()` calls | Revert `Program.cs` changes | No OTel registration — harmless |
| Polly callback logging | Revert `PollyResiliencePipeline.cs` changes | Back to placeholder `ValueTask.CompletedTask` |
| CI PostgreSQL container | Revert `.github/workflows/ci.yml` | Data integration tests fail in CI again |

**Note:** OpenTelemetry is purely additive — no breaking changes. Rollback is safe at any point.

---

## Monitoring Hooks

- **Console exporter:** Spans visible in `stdout` of each container
- **OTLP collector (future):** When `OTEL_EXPORTER_OTLP_ENDPOINT` env var is set, the extension should conditionally switch from console to OTLP exporter
- **Log correlation:** Trace IDs appear in all structured log messages from Kendo services

---

## Known Issues / Caveats

- **OpenTelemetry.Api vulnerability (NU1902):** Package 1.12.0 has a moderate-severity advisory (GHSA-g94r-2vxg-569j). Monitor for patch updates. The vulnerability does not affect console exporter usage in development/CI.
- **Console exporter:** Intended for local dev and CI verification. Production deployments should configure an OTLP collector endpoint (Phase 02+).
- **No manual span creation:** This milestone relies on auto-instrumentation only (`AddAspNetCoreInstrumentation`). Custom spans will be added when async messaging arrives in Phase 02.
