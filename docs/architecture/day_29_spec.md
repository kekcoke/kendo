# Architecture Spec — Day 29 — W2: Event Conflict & Schedule Reasoning

> **Milestone:** M5.8 — W2 Event Conflict & Schedule Reasoning (second P0 workload)  
> **Roadmap phase:** 05 — AI/Vector Service (FastAPI)  
> **Date:** 2026-06-20  
> **Type:** Workload spec (adopts by reference `fastapi_rag_service_spec.md §W2`)  
> **Depends on:** `docs/architecture/fastapi_rag_service_spec.md` §W2 (design-spec contract, rev 2026-06-16), Day 28 (M5.7 — pgvector has event embeddings)

---

## Milestone Scope

- **What:** Implement the W2 Event Conflict & Schedule Reasoning workload — `POST /api/events/{id}/validate` on the Gateway proxies to FastAPI `/v1/rag/validate` which runs multi-step reasoning (langgraph tool-use) to detect scheduling conflicts, headcount plausibility, and time collisions.
- **Maps to:** `fastapi_rag_service_spec.md §W2 — Event Conflict & Schedule Reasoning` (data contracts, acceptance gates)
- **Explicitly out of scope:**
  - W1 (already live from Day 28)
  - P1/P2 workloads (handled in subsequent days)
  - W8 eval gate — must already be live on `develop`

---

## Layer Changes

| Layer | Service | Change |
|-------|---------|--------|
| Application | `src/FastAPIService/app/api/v1/validate.py` | New — `POST /v1/rag/validate` endpoint |
| Application | `src/FastAPIService/app/rag/validate_chain.py` | New — langgraph reasoning chain (tool-use for date math, venue lookup, conflict detection) |
| Application | `src/Gateway/Controllers/RagController.cs` | Modify — add `POST /api/events/{id}/validate` route proxying to FastAPI |
| Application | `src/Gateway/Services/FastAPIClient.cs` | Modify — add `ValidateAsync` method |

---

## Data Contracts

Adopted by reference from `fastapi_rag_service_spec.md §W2 — Event Conflict & Schedule Reasoning` (rev 2026-06-16). Key contract:

**Gateway → FastAPI:**
```
POST /v1/rag/validate
{
  "event": { /* structured Event JSON from W1 output */ },
  "user_id": "uuid"
}
```

**FastAPI → Gateway response:**
```json
{
  "ok": true,
  "conflicts": [],
  "suggestions": [],
  "reasoning_trace": [{"step": "check_date_overlap", "result": "no conflict"}]
}
```

---

## Implementation Plan (Commit Units)

### Unit 1 — FastAPI validation endpoint + langgraph reasoning chain

**Files:**
- `src/FastAPIService/app/api/v1/validate.py` — `POST /v1/rag/validate` route
- `src/FastAPIService/app/rag/validate_chain.py` — langgraph chain with tool-use (date math, pgvector recent-events retriever, venue-lookup stub)
- `tests/fastapi/test_validate.py` — unit + integration tests

**Gate command:** `pytest tests/fastapi/test_validate.py` — must exit 0 with zero false-negatives on conflict regression set.

**Commit message:**
```
feat(fastapi): add W2 validate endpoint — multi-step conflict reasoning via langgraph

POST /v1/rag/validate takes candidate Event + user_id, queries pgvector for
recent events, runs langgraph tool-use chain (date overlap, headcount plausibility,
recurring-event collision), returns ValidationResult with reasoning trace.
Implements fastapi_rag_service_spec.md §W2.

Day 29 — M5.8 Unit 1 of 2 | Milestone: M5.8 — W2 Event Conflict & Schedule Reasoning
Coverage: Zero false-negatives on conflict regression set
Lint: clean
```

### Unit 2 — Gateway route: POST /api/events/{id}/validate

**Files:**
- `src/Gateway/Controllers/RagController.cs` — add `POST /api/events/{id}/validate`
- `src/Gateway/Services/IFastAPIClient.cs` — add `ValidateAsync`
- `src/Gateway/Services/FastAPIClient.cs` — implement `ValidateAsync`
- `tests/Kendo.Tests/Integration/FastAPIWorkloadTests.cs` — add W2 integration tests

**Gate command:** `dotnet test tests/Kendo.Tests --filter "Category=FastAPIW2"` — must exit 0.

**Commit message:**
```
feat(gateway): add W2 validate route — POST /api/events/{id}/validate proxies to FastAPI

Creates ValidateAsync on IFastAPIClient. Gateway returns reasoning_trace in
response for auditability. RFC 7807 on FastAPI failure.

Day 29 — M5.8 Unit 2 of 2 | Milestone: M5.8 — W2 Event Conflict & Schedule Reasoning
Coverage: 100% new integration tests
Lint: clean
```

---

## Success Checklist

Maps 1:1 to `fastapi_rag_service_spec.md §W2 acceptance gate`:

| # | Criterion | Maps to |
|---|-----------|---------|
| 1 | Zero false-negatives on the conflict regression test set (a conflict is never missed) | M5.8 acceptance |
| 2 | False-positive rate ≤ 5% on the conflict regression set | M5.8 acceptance |
| 3 | Reasoning trace returned with every response (array of {step, result}) | M5.8 acceptance |
| 4 | LangGraph tool-use chain completes within p95 budget (≤ 15s for multi-step) | M5.8 acceptance |
| 5 | M5.14 (W8) eval gate live on `develop` | P0 ordering rule |

---

## Resilience Mandate

- Same pybreaker + tenacity pattern as Day 28 (M5.7). No new resilience config.
- Gateway `ValidateAsync` uses existing `IFastAPIClient` Polly pipeline.
- LangGraph tool-use chain has a per-tool timeout (10s per tool call, enforced by `asyncio.wait_for`).

---

## Depends on

- `docs/architecture/fastapi_rag_service_spec.md §W2` — data contracts, acceptance gates
- Day 28 (M5.7) — pgvector has event embeddings from W1
- `src/Gateway/Services/IFastAPIClient.cs` — extended with `ValidateAsync`
