# Review Report — Day 31 (M5.11): W5 Embeddings Backfill & Re-indexing

> **Reviewer:** Kendo Reviewer Agent  
> **Date:** 2026-06-20  
> **Verdict:** ✅ PASS  
> **PR:** [#38](https://github.com/kekcoke/kendo/pull/38) — squash-merged to `develop` at `7b2b6c2`

---

## Gate Verification

| Gate | Status | Notes |
|---|---|---|
| Phase 1 — Architecture Spec | ✅ | `docs/architecture/day_31_spec.md` exists, adopts `fastapi_rag_service_spec.md §W5` by reference |
| Phase 2 — Commit Log | ✅ | 2/2 committed, zero halted |
| Phase 2 — Unit Tests | ✅ | 26/26 passing (14 reindex CLI, 6 UserServiceClient, 6 scheduler) |
| Phase 4 — CI Pipeline | ✅ | `build-and-test` ✅, `eval-gate` ✅, `docker-compose` ✅; `chaos-test` failure on base commit (known pre-existing flakiness) |
| Phase 4 — Runbook | ✅ | `ops/runbooks/day_31_runbook.md` with deployment steps, env vars, failure modes |

---

## Deliverables Reviewed

| # | Artifact | Verdict | Notes |
|---|---|---|---|
| 1 | `src/FastAPIService/app/jobs/reindex.py` | ✅ | CLI entry point, checkpoint/resume, batch pipeline, 10k limit guard |
| 2 | `src/FastAPIService/app/integrations/user_service_client.py` | ✅ | Instance-scoped pybreaker, tenacity retry, clear error types |
| 3 | `src/UserService/Controllers/EmbeddingAdminController.cs` | ✅ | Batch endpoint matches existing single upsert pattern; DTOs aligned |
| 4 | `src/FastAPIService/app/jobs/checkpoint.py` | ✅ | Atomic write, stale lock detection, configurable path |
| 5 | `src/FastAPIService/app/jobs/scheduler.py` | ✅ | apscheduler integration with env-var cron config |
| 6 | `src/FastAPIService/app/config.py` | ✅ | New UserService settings added |
| 7 | `src/FastAPIService/pyproject.toml` | ✅ | apscheduler dependency added |
| 8 | `tests/fastapi/test_reindex.py` | ✅ | 14 tests covering all branches |
| 9 | `tests/fastapi/test_user_service_client.py` | ✅ | 6 tests covering success/error paths |
| 10 | `tests/fastapi/test_scheduler.py` | ✅ | 6 tests covering scheduler lifecycle |
| 11 | `ops/runbooks/day_31_runbook.md` | ✅ | Complete failure mode coverage |

---

## Spec Fidelity Check

`day_31_spec.md` was compared against the implementation by reference to `fastapi_rag_service_spec.md §W5`:

| Spec Requirement | Implemented | Status |
|---|---|---|
| CLI entry point with argparse | `reindex.py` — `--since`, `--batch-size`, `--dry-run` | ✅ |
| 64-event batch processing | Batch size 64, loop pagination | ✅ |
| Idempotent batch writes (upsert) | `event_id + model_name` upsert in .NET controller | ✅ |
| Resumable (checkpoint) | `checkpoint.py` with `last_processed_event_id` | ✅ |
| Writes via UserService admin endpoint | `POST /internal/embeddings/batch` | ✅ |
| apscheduler cron (daily 02:00 UTC) | `scheduler.py` with configurable env var | ✅ |
| Checkpoint file lock protection | File-lock with stale detection (1h) | ✅ |

---

## Regression Assessment

- **No .NET schema changes** — only a new controller method added to existing class
- **No Python route changes** — only new modules added, no existing routes modified
- **All existing CI checks pass** on the feature branch (chaos-test flakiness is pre-existing)
- **Defense-in-depth preserved:** FastAPI writes through UserService admin endpoint, never directly
