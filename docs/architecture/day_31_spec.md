# Architecture Spec — Day 30 — W5: Embeddings Backfill & Re-indexing

> **Milestone:** M5.11 — W5 Embeddings Backfill & Re-indexing (P1, first P1 workload)  
> **Roadmap phase:** 05 — AI/Vector Service (FastAPI)  
> **Date:** 2026-06-20  
> **Type:** Workload spec (adopts by reference `fastapi_rag_service_spec.md §W5`)  
> **Depends on:** `docs/architecture/fastapi_rag_service_spec.md` §W5 (design-spec contract, rev 2026-06-16)

---

## Milestone Scope

- **What:** Implement W5 — a batch embeddings backfill and re-indexing job for FastAPI. `apscheduler` cron job + `python -m app.jobs.reindex` CLI. Reads rows from UserService via internal API (no direct DB write — defense-in-depth via `fastapi_ro`). Writes via UserService admin endpoint.
- **Why P1 first:** Sets up the embedding infrastructure that W3 (semantic search) reads.
- **Maps to:** `fastapi_rag_service_spec.md §W5 — Embeddings Backfill & Re-indexing` (data contracts, acceptance gates)
- **Explicitly out of scope:**
  - Admin endpoint auth between FastAPI and UserService — uses existing `admin:writes` JWT scope per CCD-3
  - W3 semantic search (handled in Day 31)

---

## Layer Changes

| Layer | Service | Change |
|-------|---------|--------|
| Application | `src/FastAPIService/app/jobs/reindex.py` | New — CLI entry point (`python -m app.jobs.reindex`) |
| Application | `src/FastAPIService/app/jobs/scheduler.py` | New — `apscheduler` cron job for periodic reindex |
| Application | `src/FastAPIService/app/integrations/user_service_client.py` | New — HTTP client to UserService admin endpoint |
| Application | `src/UserService/Controllers/EmbeddingAdminController.cs` | Modify — ensure batch-accept path exists (returns 202 with job ID) |

---

## Data Contracts

Adopted by reference from `fastapi_rag_service_spec.md §W5 — Embeddings Backfill & Re-indexing` (rev 2026-06-16).

**New FastAPI ↔ UserService integration contract (extending CCD-3):**

- FastAPI reads events from UserService via `GET /api/events?since=<timestamp>&limit=64`
- FastAPI computes embeddings in batches of 64
- FastAPI writes embeddings back via `POST /internal/embeddings/batch` on UserService's `EmbeddingAdminController`
- Both endpoints require service-JWT with `admin:writes` scope

**Checkpoint/resume contract:**
- Job state file at `/var/kendo/reindex_checkpoint.json` — contains `last_processed_event_id` and `last_processed_at`
- Resumable: on restart, reads checkpoint and resumes from that position
- Idempotent: UserService's admin endpoint uses `event_id + model_version` as upsert key

---

## Implementation Plan (Commit Units)

### Unit 1 — FastAPI reindex CLI + UserService batch admin endpoint

**Files:**
- `src/FastAPIService/app/jobs/reindex.py` — CLI entry point with argparse (`--since`, `--batch-size`, `--dry-run`)
- `src/FastAPIService/app/integrations/user_service_client.py` — HTTP client for UserService reads + writes
- `src/UserService/Controllers/EmbeddingAdminController.cs` — add `POST /internal/embeddings/batch` (returns 202 with job ID)
- `tests/fastapi/test_reindex.py` — unit tests for CLI + client
- `tests/Kendo.Tests/Integration/EmbeddingAdminTests.cs` — integration test for batch endpoint

**Gate command:** `python -m app.jobs.reindex --since 2026-01-01 --batch-size 64 --dry-run` + `dotnet test tests/Kendo.Tests --filter "Category=EmbeddingAdmin"`

**Commit message:**
```
feat(fastapi+userservice): add W5 embeddings backfill CLI and batch admin endpoint

Adds python -m app.jobs.reindex CLI with checkpoint/resume and idempotent batch
writes. UserService EmbeddingAdminController gains POST /internal/embeddings/batch
(202 + job ID). 64-event batch size, upsert by event_id+model_version.
Implements fastapi_rag_service_spec.md §W5.

Day 30 — M5.11 Unit 1 of 2 | Milestone: M5.11 — W5 Embeddings Backfill & Re-indexing
Coverage: 100% CLI + batch endpoint tests
Lint: clean
```

### Unit 2 — apscheduler cron + checkpoint persistence

**Files:**
- `src/FastAPIService/app/jobs/scheduler.py` — apscheduler cron job, configurable interval (default daily at 02:00 UTC)
- `src/FastAPIService/app/jobs/checkpoint.py` — checkpoint read/write with file lock
- `tests/fastapi/test_scheduler.py` — cron job unit tests

**Gate command:** `pytest tests/fastapi/test_scheduler.py`

**Commit message:**
```
feat(fastapi): add apscheduler cron for periodic embeddings reindex

Daily 02:00 UTC reindex job with checkpoint/resume. File-lock protected
checkpoint at /var/kendo/reindex_checkpoint.json. Configurable interval
via FASTAPI__REINDEX__SCHEDULE env var.

Day 30 — M5.11 Unit 2 of 2 | Milestone: M5.11 — W5 Embeddings Backfill & Re-indexing
Coverage: 100% scheduler tests
Lint: clean
```

---

## Success Checklist

Maps 1:1 to `fastapi_rag_service_spec.md §W5 acceptance gate`:

| # | Criterion | Maps to |
|---|-----------|---------|
| 1 | 10k events re-embedded in ≤ 5 minutes on a 2-core container | M5.11 acceptance |
| 2 | Idempotent: re-running produces no duplicate embeddings | M5.11 acceptance |
| 3 | Resumable: process death mid-run resumes from last checkpoint, not from zero | M5.11 acceptance |
| 4 | Writes go through UserService admin endpoint, not direct DB (fastapi_ro enforced) | Defense-in-depth |
| 5 | Checkpoint file is lock-protected against concurrent job runs | Operational safety |

---

## Resilience Mandate

- Retry on each 64-event batch: up to 3 retries with exponential backoff (tenacity)
- Circuit breaker on UserService admin endpoint (wrapped in pybreaker, inherited pattern)
- Job timeout: max 30 minutes runtime; if exceeded, job logs warning and exits. Next cron run resumes from checkpoint
- File-lock on checkpoint prevents concurrent job runs

---

## Depends on

- `docs/architecture/fastapi_rag_service_spec.md §W5` — data contracts, acceptance gates
- M5.2+M5.3 (Day 22) — pgvector read access + embedding model available
- Day 20 (M0.6) — `EmbeddingAdminController` exists with `admin:writes` scope
- Day 26 (CF-2) — service-JWT issuance available for FastAPI → UserService calls
