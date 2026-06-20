# Architecture Spec — Day 26 — Chaos Test CI Flakiness Fix

> **Carry-forward resolution:** CF-1 — `test_db_downtime` intermittent failure  
> **Roadmap phase:** 05 — AI/Vector Service (FastAPI)  
> **Date:** 2026-06-20  
> **Type:** Remediation spec — no new architectural contracts  
> **Depends on:** `ops/runbooks/fastapi_service.md` §Known CI Flakiness

---

## Milestone Scope

- **What:** Fix the `test_db_downtime` chaos test that intermittently returns `000000` (connection refused) instead of expected 503 in CI.
- **Root cause (suspected):** Race condition between `docker compose unpause` completing and the PostgreSQL health check re-evaluating. The test sends a query before PG is fully resumed.
- **Explicitly out of scope:**
  - Any application code changes (no .NET or Python changes)
  - `IssuerSigningKeyResolver` refactor (handled by Day 27 / CF-2)
  - New chaos scenarios

---

## Layer Changes

| Layer | Service | Change |
|-------|---------|--------|
| CI | `scripts/chaos/test_db_downtime.sh` | Add retry loop with health-check polling after `docker compose unpause` before asserting 503 |

---

## Data Contracts

N/A — no API or message contract changes. This is a bash-script + CI-assertion fix only.

---

## Implementation Plan (Commit Units)

### Unit 1 — Fix PG-downtime chaos test flakiness

**Files:**
- `scripts/chaos/test_db_downtime.sh`

**Change summary:** After `docker compose unpause postgres`, replace the single `curl` assertion with a retry loop that polls `docker compose exec postgres pg_isready` (up to 15s, 1s interval) before asserting the 503. This matches mitigation #2 from `ops/runbooks/fastapi_service.md` §Known CI Flakiness.

**Reference implementation (logic to apply):**
```bash
# After: docker compose unpause postgres
# Before asserting 503, wait for PG to be healthy
RETRIES=15
until docker compose exec -T postgres pg_isready -q 2>/dev/null || [ $RETRIES -eq 0 ]; do
  sleep 1
  RETRIES=$((RETRIES - 1))
done
```

**Gate command:** `scripts/chaos/run_all.sh` — must exit 0 with no `000000` failures in the DB-downtime scenario (run 3x locally to verify non-flakiness).

**Regression guard:** Run the FastAPI chaos suite (`pytest tests/fastapi/ --chaos`) to confirm no FastAPI chaos tests were broken by the script change.

**Commit message:**
```
fix(chaos): add pg_isready retry loop after unpause to eliminate test_db_downtime flakiness

Resolves CF-1 (Day 13/15/16 carry-forward). After `docker compose unpause postgres`,
poll pg_isready (up to 15s, 1s interval) before asserting 503. Eliminates the race
condition where curl fires before PG connection pool reopens.

Day 26 — CF-1 | Milestone: CF-1 (carry-forward)
Coverage: N/A (bash script only)
Lint: N/A
```

---

## Success Checklist

| # | Criterion | Maps to |
|---|-----------|---------|
| 1 | `scripts/chaos/test_db_downtime.sh` runs cleanly in local Docker Compose 3x without `000000` failure | CF-1 resolution |
| 2 | FastAPI chaos suite (`pytest tests/fastapi/ --chaos`) still passes after script change | Regression guard |
| 3 | No `000000` entries appear in CI chaos-test job artifacts after the fix | CF-1 resolution |
| 4 | Entry removed from `current_state.md` §Carry-Forward Items post-merge | State update |

---

## Resilience Mandate

N/A — the fix makes the test more robust against timing variability. No production resilience change.

---

## Depends on

- `ops/runbooks/fastapi_service.md` §Known CI Flakiness — documents the mitigation approach this spec implements
- `scripts/chaos/test_db_downtime.sh` — the file being modified
