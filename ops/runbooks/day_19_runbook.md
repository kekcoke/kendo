# Day 19 Runbook — Worker AI Handlers

> **Milestone:** M0.5 — Worker AI handlers + direct FastAPI call (W7)  
> **Date:** 2026-06-19  
> **Branch:** `feature/day-19-worker-ai-handlers`  

---

## Component Overview

| Component | Purpose |
|-----------|---------|
| `EventIngestedHandler` | Consumes `EventIngestedEvent` — writes Event row + embedding to UserService, emits `EventValidatedEvent` for W2 |
| `EventValidatedHandler` | Consumes `EventValidatedEvent` — calls FastAPI W2 for conflict validation, persists `EventValidation` row, emits `NotificationRequestedEvent` on conflict |
| `UserEmbeddingUpdatedHandler` | Consumes `UserEmbeddingUpdatedEvent` — emits welcome notification on first_embed, audit-logs reindex/model_swap |
| `NotificationRequestedHandler` | Consumes `NotificationRequestedEvent` — calls FastAPI W7 via SSE summarization, enqueues rendered body to dispatcher |
| `IFastAPISummarizationClient` | Worker-specific FastAPI client for W7 — independent Polly pipeline (15s timeout, own circuit breaker) |
| `NotificationDispatcherHostedService` | BackgroundService consuming `NotificationDispatchJob` items from in-memory channel |
| `NotificationDispatcherChannel` | Singleton in-memory `Channel<NotificationDispatchJob>` — shared between handler (producer) and hosted service (consumer) |

---

## Configuration

### Required Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `Rebus__ConnectionString` | — | ASB connection string (shared, same as Day 18) |
| `Kendo__FastApi__Summarization__TimeoutSeconds` | 15 | W7 timeout (shorter than Gateway's 30s) |
| `Kendo__FastApi__Summarization__BaseUrl` | `http://fastapi:8000` | FastAPI service endpoint (Worker-side) |
| `Kendo__FastApi__Summarization__CircuitBreakerFailures` | 3 | Number of failures before W7 breaker opens |
| `Kendo__FastApi__Summarization__CircuitBreakerBreakSeconds` | 30 | W7 breaker open duration |
| `Kendo__Worker__ServiceJwt__Audience` | — | Audience claim for service JWTs minted by Worker |

### Graceful Skip

All new registrations gracefully skip when their dependent config is missing:
- `AddKendoFastApiSummarizationClient` — skips if `Kendo__FastApi__Summarization:BaseUrl` is absent (Worker starts, but W7 calls will fail at runtime)
- `AddKendoRebusAiConsumer` (inherited from Day 18) — skips if `Rebus__ConnectionString` is empty

---

## Deployment Steps

### Prerequisite
- [ ] `Rebus__ConnectionString` configured (for Rebus consumer on `kendo-events-ai`)
- [ ] `Kendo__FastApi__Summarization__TimeoutSeconds` configured (recommended: 15)
- [ ] `Kendo__Worker__ServiceJwt__Audience` configured (for Worker → UserService direct calls)

### Deployment
1. Deploy `src/Shared/Kendo.Shared.Http/` — new `IFastAPISummarizationClient` and DTOs
2. Deploy `src/Worker/` — new handlers, dispatcher, summarization client, and Program.cs wiring
3. The Worker will auto-start consuming AI events from `kendo-events-ai` on restart

### Verification
```bash
# Build and start the Worker
docker compose build worker
docker compose up -d worker

# Check that handlers registered
docker compose logs worker | grep -i "EventIngested\|EventValidated\|NotificationRequested\|UserEmbeddingUpdated"

# Expected log output (on first event):
# Processing EventIngestedEvent: EventId=... CorrelationId=...
# Processing EventValidatedEvent: EventId=... CorrelationId=...
# Processing NotificationRequestedEvent: UserId=... TemplateId=...
```

---

## Rollback

### If AI handlers are processing incorrectly:
1. Remove the 4 handler registrations from `Worker/Program.cs`:
   - `AddTransient<IHandleMessages<EventIngestedEvent>, EventIngestedHandler>()`
   - `AddTransient<IHandleMessages<EventValidatedEvent>, EventValidatedHandler>()`
   - `AddTransient<IHandleMessages<UserEmbeddingUpdatedEvent>, UserEmbeddingUpdatedHandler>()`
   - `AddTransient<IHandleMessages<NotificationRequestedEvent>, NotificationRequestedHandler>()`
2. Remove `AddKendoFastApiSummarizationClient()` and `NotificationDispatcherHostedService` registration
3. Rebuild and redeploy Worker
4. Events will remain on `kendo-events-ai` queue (unconsumed) until handlers are restored

### If W7 summarization is failing:
- Increase `Kendo__FastApi__Summarization__TimeoutSeconds` (default 15s) if FastAPI is slow
- Decrease `CircuitBreakerFailures` if W7 is frequently unavailable (fewer failures before breaker opens)
- Restart Worker to pick up new config

---

## Known Issues

- **Notifications are ephemeral:** `NotificationDispatcherHostedService` uses an in-memory channel. A Worker crash during delivery loses the in-flight notification. The `NotificationRequestedEvent` on Rebus provides at-least-once delivery semantics; the dispatcher will re-process on restart.
- **W7 circuit breaker is independent:** The Gateway's W1/W2 breaker and the Worker's W7 breaker are separate. A slow W7 summarization will NOT affect Gateway → FastAPI traffic for W1/W2.
- **Worker → UserService direct call:** This is the first direct service-to-service call. The Worker uses a service JWT with `admin:writes` scope. If the JWT minter is misconfigured, direct writes will fail with 401/403 from UserService.
- **FastAPI not yet deployed:** W7 calls WILL fail at runtime until Phase 05 (FastAPI service) is deployed. The circuit breaker will catch these failures and log warnings. No crash-loop is expected.

---

## CI Integration

The existing CI pipeline (`build-and-test`) covers:
1. ✅ All 121+ unit tests (including 2 new FastAPISummarizationClient tests)
2. ✅ Compilation verification for all 3 service projects
3. ✅ Docker compose config validation

CI does not require a live FastAPI instance — `FastAPISummarizationClient` is tested via mocked HTTP handler in unit tests.
