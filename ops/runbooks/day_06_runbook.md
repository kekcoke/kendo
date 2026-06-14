# Ops Runbook — Day 6

> **Milestone:** M2.1 — Rebus + Azure Service Bus wired: producer and consumer registered in DI; bus starts cleanly
> **Services:** Gateway (port 5000), UserService (port 5001), Worker (port 5002)
> **Date:** 2026-06-13

---

## Health & Observability Contract

| Service | Liveness Probe | Readiness Probe | Expected Response |
|---------|---------------|-----------------|-------------------|
| Gateway | `GET :5000/health/live` | `GET :5000/health/ready` | `200 OK` — `Healthy` / `Ready` (unchanged) |
| UserService | `GET :5001/health/live` | `GET :5001/health/ready` | `200 OK` — `Healthy` / `Ready` (unchanged) |
| Worker | `GET :5002/health/live` | `GET :5002/health/ready` | `200 OK` — `Healthy` / `Ready` (unchanged) |

**Error response contract:** Unchanged — all non-health endpoints return `application/problem+json` bodies on 4xx/5xx responses (RFC 7807).

---

## What Changed This Day

| Change | Impact |
|---|---|
| `KendoMessage` abstract record in `Kendo.Shared` | Base type for all domain messages — provides `MessageId` + `CreatedAt` |
| `KendoRebusConfiguration.AddKendoRebus()` in `Kendo.Shared` | One-liner Rebus registration with Azure Service Bus transport; producer and consumer modes |
| Rebus wired as producer into Gateway + UserService | `IBus` available for `bus.Send()` — no messages sent yet (M2.2) |
| Rebus wired as consumer into Worker | `IBus` configured to poll `kendo-events` queue with 3 workers, 10 max parallelism — no handlers registered yet (M2.3) |
| Graceful-skip path for missing connection string | Services start without ASB in local dev — `IBus` resolves to `null` |
| 4 new Rebus registration smoke tests | Coverage for producer/consumer registration + graceful-skip scenarios |

### Rebus Configuration

```json
{
  "Rebus": {
    "ConnectionString": "Endpoint=sb://...",        // env: Rebus__ConnectionString
    "QueueName": "kendo-events",
    "NumberOfWorkers": 3,
    "MaxParallelism": 10
  }
}
```

### Wire Protocol

| Mode | Transport | Queue | Workers | Auto-start |
|---|---|---|---|---|
| Producer | Azure Service Bus (one-way client) | — | 1 | No — bus available for `Send()` only |
| Consumer | Azure Service Bus (queue) | `kendo-events` | 3 | Yes — polls queue on startup |

---

## Verification Playbook

### 1. Verify Health Endpoints Still Work (Regression)

```bash
docker compose up -d
curl -sS http://localhost:5000/health/live   # → Healthy
curl -sS http://localhost:5001/health/live   # → Healthy
curl -sS http://localhost:5002/health/live   # → Healthy
```

### 2. Verify Rebus Registers Without ASB (Local Dev)

```bash
# Build and run without Rebus__ConnectionString set
docker compose up -d
docker compose logs gateway | grep -i rebus
# Expected: no crash, no ASB connection attempt — bus gracefully skipped
```

### 3. Verify Producer DI Resolution (Integration)

```bash
# Can be verified via the existing test suite (run locally):
dotnet test tests/Kendo.Tests --filter "Category=Messaging"
# Expected: 4/4 passed, 0 failed
```

### 4. Verify CI Pipeline

```bash
# All tests must pass:
# - Unit: health check + ProblemDetails middleware tests (Category=Unit)
# - Data: existing integration tests (Category=Data)
# - Resilience: existing circuit breaker + retry tests (Category=Resilience)
# - Messaging: Rebus registration smoke tests (Category=Messaging)  ← NEW
# - Docker Compose: health endpoint accessibility
```

---

## CI Changes (this day)

The `Wait for healthy` step was hardened to wait for **all 4** services (gateway, userservice, worker, postgres) instead of exiting on the first healthy container. This fixes a race condition where the Gateway's Kestrel hadn't finished binding before health probes fired.

---

## Rollback Plan

### Rollback Strategy
1. **If PR not merged:** Close PR. Branch is `feature/day-06-rebus-azure-service-bus-wired`.
2. **If merged:** `git revert` the merge commit on `develop`.
3. **Verify:** All health endpoints return `200 OK`; all existing tests pass.

### Rollback Scope

| Component | Rollback Action | Impact |
|---|---|---|
| `KendoMessage` + `KendoRebusConfiguration` | Revert `src/Shared/Messaging/` | No Rebus registration available |
| `Program.cs` changes (all 3 services) | Revert `AddKendoRebus()` lines | Services no longer register Rebus |
| `.csproj` Rebus package references | Revert package additions | Dependabot no longer tracks Rebus versions |
| CI messaging step | Revert CI step addition | Messaging tests not executed in CI (no regression) |
| CI `Wait for healthy` fix | **DO NOT REVERT** — this fixes a pre-existing race condition |
| Tests | Revert `tests/Kendo.Tests/Messaging/` | 4 tests removed |

**Note:** Changes in this milestone are purely additive infrastructure wiring. No business logic changed. No existing endpoint behavior changed. Rollback is safe at any point.

---

## Known Issues / Caveats

- **No message handlers registered yet:** The consumer mode (Worker) configures Rebus to poll the `kendo-events` queue with 3 workers, but no `IHandleMessages<T>` implementations exist yet. M2.3 will add the first handler with idempotency enforcement.
- **ASB connection string required for runtime messaging:** Without `Rebus__ConnectionString`, the bus is gracefully skipped (`IBus` resolves to `null`). Any code attempting `await bus.Send(...)` will throw a `NullReferenceException` until M2.2 adds the guard. This is intentional — the guard will ensure `IBus` is not null before sending.
- **No DLQ consumer yet:** If messages fail in the consumer, they go to the ASB dead-letter queue automatically, but no DLQ handler exists. M2.4 addresses this.
- **No outbox pattern yet:** Messages sent via the producer are not backed by a transactional outbox. M2.5 addresses this.
