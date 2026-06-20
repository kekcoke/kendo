# Day 28 Runbook — M5.7: W1 Event Ingestion RAG

> **Milestone:** M5.7 — W1 Event Ingestion RAG (first P0 workload)  
> **Branch:** `feature/day-28-w1-event-ingestion`  
> **Date:** 2026-06-20  
> **Status:** 🟡 Pre-merge (blocked on M5.14)

---

## What ships

- **FastAPI:** `POST /v1/rag/ingest` — free-form event text → structured Event JSON via LangChain + pgvector retrieval
- **Gateway:** `POST /api/events/ingest` — proxied to FastAPI via existing `IFastAPIClient` with Polly pipeline
- **No new infrastructure:** reuses existing `fastapi_ro` pgvector role, skip any Dockerfile/CI changes

## Deployment

Not yet merged to `develop`. Blocked by M5.14 (W8 — Eval Gate) per P0 ordering rule.

### Pre-merge checklist
- [ ] M5.14 (W8) is live on `develop`
- [ ] Rebase `feature/day-28-w1-event-ingestion` onto updated `develop`
- [ ] Add W1 eval dataset to W8 suite
- [ ] Run `pytest tests/fastapi/eval/` — all metrics ≥ acceptance thresholds
- [ ] Full CI green
- [ ] PR squash-merge to `develop`

## Verification

### Local smoke test (FastAPI)
```bash
curl -X POST http://localhost:8000/v1/rag/ingest \
  -H "Content-Type: application/json" \
  -d '{"text": "Birthday party for Bob, Saturday at 7pm, 12 guests, no nuts"}' \
  -H "Authorization: Bearer $(cat /tmp/test-jwt)"
```

Expected: `200 OK` with structured Event JSON containing `name`, `date`, `time`, `headcount`, `dietary_notes`.

### Gateway smoke test
```bash
curl -X POST http://localhost:5000/api/events/ingest \
  -H "Content-Type: application/json" \
  -d '{"text": "Team standup tomorrow at 9am"}' \
  -H "Authorization: Bearer $(cat /tmp/test-jwt)"
```

Expected: `202 Accepted` with `Location: /api/events/{eventId}`.

### Edge cases
| Test | Expected |
|------|----------|
| Empty text | `400` with RFC 7807 body |
| FastAPI down | `503` from Gateway circuit breaker |
| pgvector unreachable | `503` from FastAPI with RFC 7807 |

## Rollback

If W1 ingest causes regression post-merge:
1. Identify the commit: `git log --oneline --all --grep="M5.7"`
2. Revert: `git revert <sha>` on `develop`
3. Notify team via changelog entry

## Dependencies
- `docs/architecture/day_28_spec.md` — spec authority
- `docs/architecture/fastapi_rag_service_spec.md §W1` — data contracts
- Existing `fastapi_ro` pgvector role (no new migration)
- Existing `IFastAPIClient` / `FastAPIClient` / `RagController` (pre-existing from prior sessions)

## Open Items (carried forward to merge day)
- [ ] W1 eval dataset (to be wired into W8 suite on merge day)
- [ ] DeepEval grounded rate ≥ 90% verification
