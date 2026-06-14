# Day 16 Architecture Specification — M3.6 Ops Runbooks

## Milestone Scope
- **Milestone:** M3.6 — Runbook complete: documented recovery playbook for each failure scenario in `ops/runbooks/`
- **Roadmap phase:** 03 — High Availability & Chaos Testing
- **Components touched:** Ops Runbooks (one file per failure scenario)
- **Existing runbooks referenced (not modified):**
  - `ops/runbooks/day_13_runbook.md` — Chaos test procedures
  - `ops/runbooks/day_14_runbook.md` — Rate limiting & load shedding
  - `ops/runbooks/day_15_runbook.md` — Graceful shutdown
- **Explicitly out of scope:**
  - M3.1–M3.5 components are already complete and passing — no code changes
  - Phase 04 (observability) — not yet started
  - Any code-level changes to services, middleware, or infrastructure config

## Layer Changes

No services, databases, or infrastructure are modified. This milestone is exclusively documentation and operational playbooks.

| Area | Change |
|---|---|
| `ops/runbooks/` | **New** — create one scenario-specific runbook per failure mode |
| `docs/platform_roadmap.md` | **Update** — mark M3.6 Ops Runbooks component ✅ after delivery |
| `.ai/current_state.md` | **Update** — mark carry-forward chaos-test flakiness as documented (if resolved) or carry forward cleanly |

## Data Contracts

None — no API schemas, message schemas, or DB migrations.

## Implementation Plan (Commit Units)

### Unit 1 — DB failover runbook
- **Files:**
  - `ops/runbooks/db-failover.md`
- **Gate command:** `[[ -f ops/runbooks/db-failover.md ]] && echo OK || echo FAIL`
- **Commit message:** `docs(runbook): add DB failover recovery playbook

  Day 16 — unit 1 of 5 | Milestone M3.6
  Covers: PostgreSQL crash detection, circuit breaker behavior, recovery validation, connection string rotation, and the known CI flakiness in test_db_downtime (carry-forward from Day 15).`

### Unit 2 — Service crash recovery runbook
- **Files:**
  - `ops/runbooks/service-crash-recovery.md`
- **Gate command:** `[[ -f ops/runbooks/service-crash-recovery.md ]] && echo OK || echo FAIL`
- **Commit message:** `docs(runbook): add service crash & replica recovery playbook

  Day 16 — unit 2 of 5 | Milestone M3.6
  Covers: NGINX health check detection, replica failover, traffic verification via X-Kendo-Replica headers, and load balancer reconvergence timing.`

### Unit 3 — DLQ drain & network partition runbook
- **Files:**
  - `ops/runbooks/dlq-drain.md`
- **Gate command:** `[[ -f ops/runbooks/dlq-drain.md ]] && echo OK || echo FAIL`
- **Commit message:** `docs(runbook): add DLQ drain & network partition playbook

  Day 16 — unit 3 of 5 | Milestone M3.6
  Covers: DLQ depth monitoring alert response, manual DLQ drain procedure, outbox relay recovery after network partition, and Rebus retry behaviour under partition.`

### Unit 4 — Horizontal scaling runbook
- **Files:**
  - `ops/runbooks/horizontal-scaling.md`
- **Gate command:** `[[ -f ops/runbooks/horizontal-scaling.md ]] && echo OK || echo FAIL`
- **Commit message:** `docs(runbook): add horizontal scaling event playbook

  Day 16 — unit 4 of 5 | Milestone M3.6
  Covers: replica count scaling triggers, NGINX upstream configuration, graceful shutdown drain window sizing, statelessness validation, and Redis cache warming considerations.`

### Unit 5 — Central recovery index
- **Files:**
  - `ops/runbooks/day_16_runbook.md`
  - `.ai/current_state.md` (carry-forward resolution)
- **Gate command:** `[[ -f ops/runbooks/day_16_runbook.md ]] && echo OK || echo FAIL`
- **Commit message:** `docs(runbook): add central recovery index and resolve carry-forward

  Day 16 — unit 5 of 5 | Milestone M3.6
  Central index references all 4 scenario playbooks. Documents known chaos flakiness in test_db_downtime per carry-forward from Day 13/15.`

## Success Checklist

- [ ] **`ops/runbooks/db-failover.md`** — documents PostgreSQL crash detection, circuit breaker fallback (503 RFC 7807), recovery validation (`curl` commands), connection string rotation procedure, and the known CI flakiness for `test_db_downtime`
- [ ] **`ops/runbooks/service-crash-recovery.md`** — documents NGINX health check TTL (8s expected), replica failover verification, traffic distribution check via `X-Kendo-Replica` headers, and rollback sequence for service redeployment
- [ ] **`ops/runbooks/dlq-drain.md`** — documents DLQ depth exceedance response (threshold: 5, poll: 60s), manual DLQ drain steps via `DlqDepthMonitor` config toggle, outbox relay recovery timeline (polls every 5s), and Rebus retry guarantees under network partition
- [ ] **`ops/runbooks/horizontal-scaling.md`** — documents scaling triggers and limits, NGINX upstream de/re-registration flow, graceful shutdown drain window sizing (`GracefulShutdown__TimeoutSeconds`), statelessness enforcement from M3.2, and Redis cache-warming strategy
- [ ] **`ops/runbooks/day_16_runbook.md`** — exists as a central index referencing all 4 scenario playbooks plus the existing day runbooks (day_13, day_14, day_15), and documents the known chaos-test CI flakiness carry-forward with mitigation notes
- [ ] All Phase 01, Phase 02, and Phase 03 prior acceptance criteria remain passing (no code changes — enforced by not modifying any source or infrastructure files)

## Resilience Mandate

N/A — this milestone produces no code, no API endpoints, no data access patterns. The runbooks document existing resilience mechanisms (Polly circuit breakers, Graceful Shutdown, Rate Limiting, Load Shedding, Outbox pattern, Rebus retry) — no new resilience configuration is introduced.

The single exception is **documentation of known flakiness**: the `test_db_downtime` chaos test (from Day 13/15 carry-forward) is documented in both `db-failover.md` and `day_16_runbook.md` with the observed symptom (`Connection refused 000000`), suspected root cause (race between PG resume and health check in CI), and recommended mitigation (increase `--health-retries` or add retry logic in the bash test script).
