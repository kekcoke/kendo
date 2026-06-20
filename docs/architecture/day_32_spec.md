# Architecture Spec — Day 31 — W3: User Profile Semantic Search

> **Milestone:** M5.9 — W3 User Profile Semantic Search (P1)  
> **Roadmap phase:** 05 — AI/Vector Service (FastAPI)  
> **Date:** 2026-06-20  
> **Type:** Workload spec (adopts by reference `fastapi_rag_service_spec.md §W3`)  
> **Depends on:** `docs/architecture/fastapi_rag_service_spec.md` §W3 (design-spec contract, rev 2026-06-16), Day 30 (M5.11 — W5 sets up user_embeddings)

---

## Milestone Scope

- **What:** Implement W3 — `GET /api/users/search?q=...` on the Gateway proxies to FastAPI `/v1/users/search` which runs hybrid search (pgvector cosine + Postgres BM25 full-text) against the `user_embeddings` table seeded by W5.
- **Maps to:** `fastapi_rag_service_spec.md §W3 — User Profile Semantic Search` (data contracts, acceptance gates)
- **Explicitly out of scope:**
  - Embedding column migration — W5 already created `user_embeddings`
  - W5 backfill job — already live

---

## Layer Changes

| Layer | Service | Change |
|-------|---------|--------|
| Application | `src/FastAPIService/app/api/v1/user_search.py` | New — `GET /v1/users/search` endpoint |
| Application | `src/FastAPIService/app/repositories/user_search.py` | New — hybrid search (pgvector cosine + Postgres full-text BM25 via `ts_rank`) |
| Application | `src/Gateway/Controllers/UserSearchController.cs` | Modify — proxy to FastAPI instead of stub |
| Application | `src/Gateway/Services/FastAPIClient.cs` | Modify — add `UserSearchAsync` method |

---

## Data Contracts

Adopted by reference from `fastapi_rag_service_spec.md §W3 — User Profile Semantic Search` (rev 2026-06-16).

**Gateway → FastAPI:**
```
GET /v1/users/search?q=string&top_k=10
```

**FastAPI → Gateway response:**
```json
{
  "results": [
    {"user_id": "uuid", "score": 0.92, "matched_field": "bio"}
  ],
  "trace_id": "string"
}
```

Gateway resolves user IDs → full profiles via UserService and returns the complete list.

---

## Implementation Plan (Commit Units)

### Unit 1 — FastAPI hybrid search endpoint

**Files:**
- `src/FastAPIService/app/api/v1/user_search.py` — `GET /v1/users/search` route
- `src/FastAPIService/app/repositories/user_search.py` — hybrid retriever combining `embedding <=> $1` cosine + `ts_rank(tsvector, plainto_tsquery(...))`
- `src/FastAPIService/app/rag/ensemble_retriever.py` — `EnsembleRetriever` weighting (cosine 0.7, BM25 0.3), configurable via env var
- `tests/fastapi/test_user_search.py` — integration tests

**Gate command:** `pytest tests/fastapi/test_user_search.py` — must exit 0 with precision@10 ≥ 0.85 on labeled query set.

**Commit message:**
```
feat(fastapi): add W3 user semantic search — hybrid pgvector cosine + BM25

GET /v1/users/search runs EnsembleRetriever with configurable weight split
(default 0.7 cosine, 0.3 BM25). Returns top-N user IDs with relevance scores.
Gateway resolves IDs to full profiles via UserService.
Implements fastapi_rag_service_spec.md §W3.

Day 31 — M5.9 Unit 1 of 2 | Milestone: M5.9 — W3 User Profile Semantic Search
Coverage: precision@10 ≥ 0.85
Lint: clean
```

### Unit 2 — Gateway route

**Files:**
- `src/Gateway/Controllers/UserSearchController.cs` — modify existing controller to proxy to FastAPI
- `src/Gateway/Services/IFastAPIClient.cs` — add `UserSearchAsync`
- `src/Gateway/Services/FastAPIClient.cs` — implement `UserSearchAsync`
- `tests/Kendo.Tests/Integration/FastAPIWorkloadTests.cs` — add W3 tests

**Gate command:** `dotnet test tests/Kendo.Tests --filter "Category=FastAPIW3"` — must exit 0.

**Commit message:**
```
feat(gateway): proxy W3 user search to FastAPI with existing Polly pipeline

Gateway UserSearchController forwards GET /api/users/search to FastAPI
GET /v1/users/search via IFastAPIClient.UserSearchAsync. Result IDs resolved to
full profiles via UserService before returning to client.

Day 31 — M5.9 Unit 2 of 2 | Milestone: M5.9 — W3 User Profile Semantic Search
Coverage: 100% new integration tests
Lint: clean
```

---

## Success Checklist

Maps 1:1 to `fastapi_rag_service_spec.md §W3 acceptance gate`:

| # | Criterion | Maps to |
|---|-----------|---------|
| 1 | precision@10 ≥ 0.85 on the labeled query set | M5.9 acceptance |
| 2 | Hybrid search latency p95 ≤ 300ms | M5.9 acceptance |
| 3 | Ensemble weight split configurable via env var (FastAPI restart) | Operational flexibility |
| 4 | Gateway resolves user IDs to full profiles before returning | UX completeness |

---

## Resilience Mandate

- Same pybreaker + tenacity pattern as foundation. No new resilience config.
- Gateway `UserSearchAsync` uses existing `IFastAPIClient` Polly pipeline.
- Hybrid search failure falls back to pure BM25 (cosine fails → return BM25 results; BM25 fails → return empty array, not 500).

---

## Depends on

- `docs/architecture/fastapi_rag_service_spec.md §W3` — data contracts, acceptance gates
- Day 30 (M5.11) — `user_embeddings` table populated by W5 backfill
- `src/FastAPIService/app/db.py` — existing asyncpg pool (extend with full-text query)
