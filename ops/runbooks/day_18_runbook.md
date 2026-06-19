# Day 18 Runbook — AI Event Contracts & Queue Topology

> **Milestone:** M0.3+M0.4 — Service Bus AI event contracts + `kendo-events-ai` queue topology  
> **Date:** 2026-06-19  
> **Branch:** `feature/day-18-ai-event-contracts-queue-topology`  

---

## Component Overview

| Component | Purpose |
|-----------|---------|
| `KendoTopology` | Centralized queue/DLQ name constants (kendo-events, kendo-events-ai, kendo-events-fastapi) |
| `EventIngestedEvent` | Published by Gateway after W1 RAG ingestion; consumed by Worker |
| `EventValidatedEvent` | Published by Worker after W2 validation; consumed by Worker for notification fanout |
| `UserEmbeddingUpdatedEvent` | Published by UserService after embed/reindex; consumed by Worker |
| `NotificationRequestedEvent` | Published by Worker for W7 notification summarization |
| `AddKendoRebusAiConsumer` | Worker-side consumer on `kendo-events-ai` (2 workers, parallelism 5) |
| `AddKendoRebusAiProducer` | Gateway/UserService-side one-way producer to `kendo-events-ai` |
| `DlqDepthMonitor (dual-queue)` | Polls both `kendo-events/$DeadLetterQueue` and `kendo-events-ai/$DeadLetterQueue` |

---

## Configuration

### Required Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `Rebus__ConnectionString` | — | ASB connection string (shared by all Rebus consumers/producers) |
| `Rebus__AiWorkers` | 2 | Number of AI consumer worker threads |
| `Rebus__AiParallelism` | 5 | Max parallelism for the AI consumer |
| `Rebus__DlqThreshold` | 5 | DLQ depth threshold for alerting (both queues) |
| `Rebus__DlqPollingIntervalSeconds` | 60 | DLQ polling interval |

### Graceful Skip

All Rebus registrations (`AddKendoRebus`, `AddKendoRebusAiConsumer`, `AddKendoRebusAiProducer`, `AddKendoRebusDlqConsumer`) gracefully skip when `Rebus__ConnectionString` is empty/missing. Local development requires no ASB.

---

## Deployment Steps

### Prerequisite
- [ ] `Rebus__ConnectionString` configured in environment or appsettings
- [ ] Azure Service Bus namespace exists with capacity for 2 queues

### Deployment
1. Deploy `src/Shared/` — the new event types and extensions are in `Kendo.Shared`
2. Deploy `src/Gateway/` — no service restart required; AI producer is registered at startup
3. Deploy `src/UserService/` — no service restart required; AI producer is registered at startup
4. Deploy `src/Worker/` — AI consumer starts on `kendo-events-ai`; DLQ monitor polls both queues

### Verification
```bash
# Verify Worker builds and starts
docker compose build worker
docker compose up -d worker
docker compose logs worker | grep "DlqDepthMonitor"

# Expected log output:
# DlqDepthMonitor: Starting with threshold=5, pollingInterval=60s. Monitoring queues: kendo-events, kendo-events-ai
```

---

## Rollback

### If AI events are publishing incorrectly:
1. Remove the `AddKendoRebusAiProducer()` call from `Gateway/Program.cs` and `UserService/Program.cs`
2. Remove the `AddKendoRebusAiConsumer()` call from `Worker/Program.cs`
3. Rebuild and redeploy all 3 services
4. The `kendo-events-ai` queue will remain in ASB but no consumers/producers will attach

### If DLQ alerts are noisy:
- Increase `Rebus__DlqThreshold` (default 5) or `Rebus__DlqPollingIntervalSeconds` (default 60)
- Restart the Worker to pick up new config

---

## Known Issues

- **Graceful skip behavior:** If `Rebus__ConnectionString` is set but invalid, the Worker will crash-loop on startup. Verify the connection string before deploying.
- **ASB queue auto-creation:** Azure Service Bus auto-creates `kendo-events-ai` on first send/receive. No manual provisioning needed in most environments.
- **Dual-queue DLQ polling:** Both queues are polled sequentially. If the first queue's `GetQueueRuntimePropertiesAsync` hangs (e.g., temporary ASB issue), the second queue's polling is delayed by the timeout. This is within the 60s polling budget.

---

## CI Integration

The existing CI pipeline (`build-and-test`) covers:
1. ✅ All 119 unit tests (including existing messaging tests)
2. ✅ Compilation verification for all 3 service projects
3. ✅ Docker compose config validation

CI does not require a live ASB instance — all new Rebus registrations gracefully skip when the connection string is absent.
