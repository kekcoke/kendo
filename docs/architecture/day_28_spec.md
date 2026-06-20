# Architecture Spec — Day 28 — W1: Event Ingestion RAG

> **Milestone:** M5.7 — W1 Event Ingestion RAG (first P0 workload)  
> **Roadmap phase:** 05 — AI/Vector Service (FastAPI)  
> **Date:** 2026-06-20  
> **Type:** Workload spec (adopts by reference `fastapi_rag_service_spec.md §W1`)  
> **Depends on:** `docs/architecture/fastapi_rag_service_spec.md` §W1 (design-spec contract, rev 2026-06-16)

---

## Milestone Scope

- **What:** Implement the W1 Event Ingestion RAG workload — `POST /api/events/ingest` on the Gateway receives free-form event text, proxies to FastAPI `/v1/rag/ingest` which chunks, embeds, retrieves from pgvector, and calls Azure OpenAI to return structured `Event` JSON.
- **Maps to:** `fastapi_rag_service_spec.md §W1 — Event Ingestion RAG` (data contracts, acceptance gates)
- **Explicitly out of scope:**
  - W2 (M5.8 — Event Conflict Reasoning) — handled in Day 29
  - W8 (M5.14 — Eval Gate) — prerequisite noted, must be live on `develop` before this ships to prod
  - User-facing token issuance (resolved by Day 26 / CF-2)

---

## Layer Changes

| Layer | Service | Change |
|-------|---------|--------|
| Application | `src/FastAPIService/app/api/v1/ingest.py` | New — `POST /v1/rag/ingest` endpoint |
| Application | `src/FastAPIService/app/rag/ingest_chain.py` | New — LangChain ingest chain: chunk → embed → retrieve → LLM → structured Event |
| Application | `src/Gateway/Controllers/RagController.cs` | Modify — add `POST /api/events/ingest` route proxying to FastAPI |
| Application | `src/Gateway/Services/FastAPIClient.cs` | Modify — add `IngestAsync` method |
| Data | No DB schema change | FastAPI reads from existing pgvector (fastapi_ro); writes to UserService via Gateway → existing `EventsController` |
| Infrastructure | Docker Compose | No change — FastAPI already on internal network |

---

## Data Contracts

Data contracts adopted by reference from `fastapi_rag_service_spec.md §W1 — Event Ingestion RAG` (rev 2026-06-16). See that document for:
- Request/response schemas
- Acceptance gates (p95 ≤ 8s, grounded ≥ 90%)
- LLM chain contract

This spec defines only the commit-unit breakdown and file-level implementation details.

**Gateway → FastAPI proxying:**
- Gateway receives `POST /api/events/ingest` with `{ "text": "string" }`
- Forwarded to FastAPI `POST /v1/rag/ingest` as `{ "text": "string", "user_id": "uuid (from JWT sub)" }`
- FastAPI returns structured `Event` JSON → Gateway validates → Gateway writes to UserService via existing `EventsController`
- Gateway returns the structured Event to the caller

---

## Implementation Plan (Commit Units)

### Unit 1 — FastAPI ingest endpoint + LangChain chain

**Files:**
- `src/FastAPIService/app/api/v1/ingest.py` — `POST /v1/rag/ingest` route
- `src/FastAPIService/app/rag/ingest_chain.py` — chunk → embed → pgvector retrieval → LLM structured output chain
- `tests/fastapi/test_ingest.py` — unit + integration tests

**Gate command:** `pytest tests/fastapi/test_ingest.py` — must exit 0 with ≥ 90% grounded rate on test fixtures.

**Commit message:**
```
feat(fastapi): add W1 ingest endpoint — free-form text to structured Event via LangChain

POST /v1/rag/ingest chunks input text, retrieves top-K similar past events from
pgvector, calls Azure OpenAI to produce structured Event JSON. Legacy context
retrieval seeded from existing event embeddings. Implements fastapi_rag_service_spec.md §W1.

Day 28 — M5.7 Unit 1 of 2 | Milestone: M5.7 — W1 Event Ingestion RAG
Coverage: ≥90% grounded rate on test fixtures
Lint: clean
```

### Unit 2 — Gateway route: POST /api/events/ingest

**Files:**
- `src/Gateway/Controllers/RagController.cs` — add `POST /api/events/ingest`
- `src/Gateway/Services/IFastAPIClient.cs` — add `IngestAsync`
- `src/Gateway/Services/FastAPIClient.cs` — implement `IngestAsync`
- `tests/Kendo.Tests/Integration/FastAPIWorkloadTests.cs` — new W1 integration tests

**Gate command:** `dotnet test tests/Kendo.Tests --filter "Category=FastAPIW1"` — must exit 0.

**Commit message:**
```
feat(gateway): add W1 ingest route — POST /api/events/ingest proxies to FastAPI

Creates IngestAsync on IFastAPIClient with existing Polly pipeline (30s timeout,
3 retry, circuit breaker). Gateway validates FastAPI's structured Event response
and writes to UserService via existing EventsController. RFC 7807 on failure.

Day 28 — M5.7 Unit 2 of 2 | Milestone: M5.7 — W1 Event Ingestion RAG
Coverage: 100% new integration tests
Lint: clean
```

---

## Success Checklist

Maps 1:1 to `fastapi_rag_service_spec.md §W1 acceptance gate`:

| # | Criterion | Maps to |
|---|-----------|---------|
| 1 | `POST /api/events/ingest` returns structured Event JSON with correct schema | M5.7 acceptance |
| 2 | p95 latency ≤ 8s (measured on integration test with real pgvector) | M5.7 acceptance |
| 3 | Grounded answer rate ≥ 90% on a held-out DeepEval test set | M5.7 acceptance |
| 4 | Embedding model parity with UserService ingestion path verified by contract test | M5.7 acceptance |
| 5 | Gateway returns RFC 7807 503 when FastAPI circuit breaker is open | Phase 05 regression guard |
| 6 | M5.14 (W8) eval gate is live on `develop` before merge to prod | P0 ordering rule |

---

## Resilience Mandate

Per `fastapi_rag_service_spec.md`:
- **Existing:** pybreaker per-dependency (pgvector, Azure OpenAI) inherited from M5.5
- **Existing:** tenacity retry with exponential backoff + jitter inherited from M5.5
- **New (this spec):** Gateway `IngestAsync` uses the existing `IFastAPIClient` Polly pipeline (30s timeout, 3 retry, circuit breaker 3 failures / 30s). No new resilience config needed.

---

## Depends on

- `docs/architecture/fastapi_rag_service_spec.md §W1` — data contracts, acceptance gates, LLM chain design
- Day 26 (CF-2) — Gateway JWKS contract frozen; key rotation active
- Day 27 (CF-1) — CI chaos tests stable
- `src/Gateway/Services/IFastAPIClient.cs` — existing interface, extended with `IngestAsync`
- `src/Gateway/Controllers/RagController.cs` — existing controller, extended with new route
