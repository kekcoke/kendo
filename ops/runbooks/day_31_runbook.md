# Day 31 Runbook — W5 Embeddings Backfill & Re-indexing (M5.11)

> **Milestone:** M5.11 — First P1 workload  
> **Service:** FastAPI + UserService  
> **Date:** 2026-06-20  
> **Maps to:** `docs/architecture/fastapi_rag_service_spec.md §W5`

---

## 1. Deployment Steps

```bash
# Ensure dependencies are installed (apscheduler added)
cd src/FastAPIService
pip install -e .
```

**New env vars (FastAPI):**

| Variable | Default | Description |
|---|---|---|
| `FASTAPI__USER_SERVICE_BASE_URL` | `http://userservice:5001` | UserService admin endpoint base URL |
| `FASTAPI__USER_SERVICE_TIMEOUT` | `30` | HTTP timeout for UserService calls |
| `FASTAPI__REINDEX__CHECKPOINT_PATH` | `/var/kendo/reindex_checkpoint.json` | Checkpoint file path |
| `FASTAPI__REINDEX__SCHEDULE` | `0 2 * * *` | Cron expression for scheduled reindex |

**New UserService endpoint:**
- `POST /internal/embeddings/batch` — batch upsert via existing `EmbeddingAdminController`
- Requires `admin:writes` JWT scope + `admin:writes` PostgreSQL role
- Returns `202 Accepted` with `{ job_id, processed, status }`

**Manual trigger:**
```bash
# Dry run
python -m app.jobs.reindex --since 2026-01-01 --batch-size 64 --dry-run

# Full backfill
python -m app.jobs.reindex --since 2026-01-01 --batch-size 64
```

---

## 2. Failure Modes

### 2.1 Checkpoint Lock Contention

**Symptom:** Logs show `Concurrent reindex detected`
**Cause:** Two reindex processes running simultaneously (manual + cron)
**Remedy:** Wait for the active run to finish. Lock auto-expires after 1 hour (stale lock removal).

### 2.2 UserService Admin Endpoint Unavailable

**Symptom:** `UserServiceClientError: UserService circuit breaker is open`
**Cause:** UserService returns 5xx repeatedly (trips pybreaker 3-failure threshold)
**Remedy:**
1. Check UserService health: `curl http://userservice:5001/health/live`
2. Resolve UserService issue
3. Breaker auto-recovers after 30s cooldown

### 2.3 Embedding Model Failure

**Symptom:** `Embedding computation failed for batch`
**Cause:** Sentence-transformers model fails to load or OOM
**Remedy:** Restart FastAPI with `--reload` or scale up container memory

### 2.4 Partial Batch Failure

**Symptom:** One event in a 64-event batch fails to embed
**Behavior:** The entire batch is retried (tenacity, up to 3 retries)
**Idempotency:** UserService upserts by `event_id + model_name`, so re-running is safe

---

## 3. Verification

```bash
# Verify checkpoint file exists after a dry run
ls -la /var/kendo/reindex_checkpoint.json

# Verify the batch endpoint responds
curl -X POST http://userservice:5001/internal/embeddings/batch \
  -H "Content-Type: application/json" \
  -d '{"embeddings": []}'
# Expected: 400 (empty list) — proves endpoint is wired

# Verify scheduler module loads
python -c "from app.jobs.scheduler import create_scheduler; print('OK')"
```
