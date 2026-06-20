# Day 27 Runbook — Chaos Test CI Flakiness Fix (CF-1)
> **Phase 4 artifact.** Created by DevOps/SRE agent.
> **Milestone:** CF-1 — `test_db_downtime` chaos test flakiness fix
> **Date:** 2026-06-20

---

## Overview

Day 27 resolves CF-1 — the `test_db_downtime` chaos test that intermittently returns `000000` (connection refused) instead of expected 503 in CI.

**Change:** After `docker compose unpause postgres`, added a pg_isready retry loop (up to 15s, 1s interval) before the recovery assertion. This eliminates the race condition where `curl` fires against UserService before the PostgreSQL connection pool reopens.

**Root cause:** `docker compose unpause` returns immediately, but PostgreSQL's pg_hba and connection listener take non-deterministic time to re-accept connections. The circuit breaker had reset, but `Npgsql` still held stale connections returning `connection refused` → curl got `000000`.

---

## Infrastructure Changes

| Resource | Type | Change |
|---|---|---|
| `scripts/chaos/test_db_downtime.sh` | Bash script | Added 15-retry pg_isready polling loop after unpause |

No Dockerfile, CI pipeline, or application code changes.

---

## Rollback Plan

1. `git revert <merge-sha>` on `develop`
2. No redeploy needed — the fix is purely in CI execution scripts

---

## Resolution

CF-1 removed from `current_state.md` §Carry-Forward Items post-merge.
