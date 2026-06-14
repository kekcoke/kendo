# Day 16 Runbook — Ops Runbooks (M3.6)
> **Phase 4 artifact.** Created by DevOps/SRE agent.  
> **Milestone:** M3.6 — Runbook complete: documented recovery playbook for each failure scenario

---

## Overview

This milestone completes the Phase 03 ops runbook set by producing 4 scenario-specific recovery playbooks plus this central index. Together with the existing day runbooks (day_13 through day_15), every failure scenario required by the Phase 03 acceptance criteria is now documented.

---

## Scenario Playbooks

| Playbook | Path | Scenario | Severity |
|---|---|---|---|
| DB Failover | [`./db-failover.md`](./db-failover.md) | PostgreSQL crash / connection loss | Critical |
| Service Crash Recovery | [`./service-crash-recovery.md`](./service-crash-recovery.md) | Container crash / health check failure | High |
| DLQ Drain & Network Partition | [`./dlq-drain.md`](./dlq-drain.md) | DLQ depth exceedance / broker or DB partition | Medium |
| Horizontal Scaling | [`./horizontal-scaling.md`](./horizontal-scaling.md) | Planned or reactive replica scaling | Low–Medium |

---

## Existing Reference Runbooks

| Runbook | Covers |
|---|---|
| [`./day_13_runbook.md`](./day_13_runbook.md) | Chaos test execution guide (DB downtime, crash, partition) |
| [`./day_14_runbook.md`](./day_14_runbook.md) | Rate limiting & load shedding configuration |
| [`./day_15_runbook.md`](./day_15_runbook.md) | Graceful shutdown configuration & verification |

---

## Failure Scenario Mapping

The following table maps each Phase 03 acceptance criterion to its recovery playbook:

| Acceptance Criterion | Playbook | Section |
|---|---|---|
| DB downtime → circuit breaker trips → fallback response | [`db-failover.md`](./db-failover.md) | Recovery Procedure, Step 3 |
| No cascading failure to upstream services during DB outage | [`db-failover.md`](./db-failover.md) | Triage Steps (Gateway note) |
| Load balancer marks replica unhealthy → traffic rerouted | [`service-crash-recovery.md`](./service-crash-recovery.md) | Multi-Replica Crash |
| Rate limiter returns 429 + Retry-After | [`horizontal-scaling.md`](./horizontal-scaling.md) | Detection — Reactive Scaling Triggers |
| Load shedding returns 503 RFC 7807 | [`horizontal-scaling.md`](./horizontal-scaling.md) | Detection — Reactive Scaling Triggers |
| Rebus retry + outbox guarantees no message loss during partition | [`dlq-drain.md`](./dlq-drain.md) | Network Partition Recovery |
| Graceful shutdown — in-flight requests drain before exit | [`horizontal-scaling.md`](./horizontal-scaling.md) | Graceful Shutdown Drain Window Sizing |
| DLQ drain procedure documented | [`dlq-drain.md`](./dlq-drain.md) | DLQ Drain Procedure |

---

## CI Flakiness: Known Issue

### `test_db_downtime` Chaos Test

**Observed symptom:** Intermittent `000000` (connection refused) returned instead of expected `503` after PostgreSQL unpause in CI.

**Tracking:** Carry-forward from Day 13 → Day 15 → Day 16. Documented in detail at:
- [`db-failover.md`](./db-failover.md) → section **Known CI Flakiness: test_db_downtime**
- This index (below)

**Status:** Deferred — root cause suspected (race condition between container resume and health check convergence). Three mitigation approaches documented in the playbook. Not blocking CI — test passes ~90% of runs.

**Recommended near-term action:** Apply mitigation #2 (retry loop after `docker compose unpause`) from `db-failover.md` to `scripts/chaos/test_db_downtime.sh`.

---

## Execution Flow Diagram

```
Failure detected
  │
  ├─ DB crash?        → db-failover.md
  ├─ Service crash?   → service-crash-recovery.md
  ├─ DLQ alert?       → dlq-drain.md
  └─ Scaling event?   → horizontal-scaling.md
        │
        ▼
  Recovery procedure
        │
        ▼
  Post-recovery verification  ← (commands in each playbook)
        │
        ▼
  Post-mortem checklist       ← (each playbook)
        │
        ▼
  Runbook updated?            ← Append new recovery steps discovered
```

---

## How to Use These Playbooks

1. **On alert/page:** Identify the scenario type (DB / Service / DLQ / Scaling)
2. **Navigate:** Open the corresponding playbook from the table above
3. **Follow procedure:** Step-by-step recovery commands — copy-paste ready
4. **Verify:** Run the post-recovery verification commands in each playbook
5. **Document:** Complete the post-mortem checklist to capture learnings
6. **Update:** If you discover new recovery steps, append to the playbook

---

## Related Documentation

- `docs/platform_roadmap.md §Phase 03` — acceptance criteria for M3.6
- `docs/architecture/day_16_spec.md` — spec that defined these playbooks
- `ops/Dockerfile` — service container configurations
- `.github/workflows/ci.yml` — CI pipeline (includes chaos-test job that validates these scenarios)
