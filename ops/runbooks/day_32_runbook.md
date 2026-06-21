# Day 32 Runbook — W3 User Profile Semantic Search (M5.9)

> **Milestone:** M5.9 — W3 User Profile Semantic Search (P1)  
> **Service:** FastAPI + Gateway  
> **Date:** 2026-06-20  
> **Maps to:** `docs/architecture/fastapi_rag_service_spec.md §W3`  
> **Deployment:** No new infrastructure — reuses existing FastAPI + pgvector + Gateway

---

## 1. Deployment Steps

No new env vars for W3. Existing FastAPI and Gateway deployment applies.

**New FastAPI module:** `src/FastAPIService/app/rag/ensemble_retriever.py` auto-registers on import. No route registration needed — `main.py` already includes the router.

**Optional env vars (ensemble weight tuning, FastAPI restart required):**

| Variable | Default | Description |
|---|---|---|
| `FASTAPI__ENSEMBLE__WEIGHT_COSINE` | `0.7` | Weight for pgvector cosine similarity |
| `FASTAPI__ENSEMBLE__WEIGHT_BM25` | `0.3` | Weight for Postgres full-text BM25 |

Weights must sum to 1.0 (not enforced — `softmax` normalized).

**Gateway deployment:** No new deployment needed. `UserSearchController.cs` and `FastAPIClient.cs` are compiled with the next Gateway release. For hotfix, copy binaries.

---

## 2. Failure Modes

### 2.1 Embedding Model Unavailable (Cosine Fails)

**Symptom:** `UserSearchRepository` returns results with all cosine scores = 0
**Cause:** pgvector `embedding <=> $1` operator fails or embedding query returns zero results
**Behavior:** `EnsembleRetriever` falls back to pure BM25 results
**Remedy:** Check pgvector health → `SELECT count(*) FROM user_embeddings`

### 2.2 BM25 Full-Text Fails

**Symptom:** `ts_rank` returns zero or error
**Cause:** Corrupted tsvector or missing `user_embeddings` GIN index
**Behavior:** `EnsembleRetriever` returns empty array (not 500)
**Remedy:**
```sql
REINDEX INDEX idx_user_embeddings_tsv;
```

### 2.3 Both Retrievers Fail

**Symptom:** Empty response from `/v1/users/search`
**Cause:** pgvector unreachable or both retrievers returned zero results
**Behavior:** Returns HTTP 200 with empty `results` array
**Remedy:** Check pgvector connection in FastAPI logs

### 2.4 Gateway Timeout

**Symptom:** Gateway returns 503 for user search
**Cause:** FastAPI response time exceeds existing Polly timeout
**Behavior:** Returns 503 with RFC 7807 body
**Remedy:** Verify FastAPI is healthy: `curl http://fastapi:8000/health/live`

---

## 3. Verification

### FastAPI smoke test
```bash
# Basic search
curl "http://localhost:8000/v1/users/search?q=event+planner&top_k=5" \
  -H "Authorization: Bearer $(cat /tmp/test-jwt)"

# Expected: 200 OK with results array sorted by descending score
```

### Gateway smoke test
```bash
curl "http://localhost:5000/api/users/search?q=event+planner&top_k=5" \
  -H "Authorization: Bearer $(cat /tmp/test-jwt)"

# Expected: 200 OK with full user profiles (resolved from UserService)
```

### Edge cases
| Test | Expected |
|------|----------|
| Empty query string | `400` with RFC 7807 body |
| Short query (< 3 chars) | Returns BM25-only results (cosine may be noisy) |
| No matches | `200` with empty `results` array |
| FastAPI down | `503` from Gateway circuit breaker |
| pgvector unreachable | Graceful fallback to BM25, no 500 |

### Integration tests
```bash
# Run FastAPI W3 tests
cd src/FastAPIService
pytest tests/fastapi/test_user_search.py -v

# Run Gateway W3 tests
cd src/Gateway
dotnet test tests/Kendo.Tests --filter "Category=FastAPIW3"
```

---

## 4. Rollback

W3 consists of additive changes (new endpoints, new client methods). No existing behavior is modified. Rollback by:
1. Revert FastAPI commit: `git revert <sha of 9b6fc0a>`
2. Revert Gateway commit: `git revert <sha of 81e191b>`
3. Revert merge: `git revert 35ce976` on `develop`

## 5. Dependencies
- `docs/architecture/day_31_spec.md` — spec authority
- `docs/architecture/fastapi_rag_service_spec.md §W3` — data contracts
- Existing `user_embeddings` table (seeded by W5 backfill)
- `src/FastAPIService/app/db.py` — asyncpg pool (extended with full-text query)
