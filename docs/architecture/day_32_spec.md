# Architecture Spec — Day 32 — W4: User Intent Classification

> **Milestone:** M5.10 — W4 User Intent Classification (P1)  
> **Roadmap phase:** 05 — AI/Vector Service (FastAPI)  
> **Date:** 2026-06-20  
> **Type:** Workload spec (adopts by reference `fastapi_rag_service_spec.md §W4`)  
> **Depends on:** `docs/architecture/fastapi_rag_service_spec.md` §W4 (design-spec contract, rev 2026-06-16)

---

## Milestone Scope

- **What:** Implement W4 — FastAPI acts as an intent *advisor* on ambiguous Gateway routes. Gateway sends request body + JWT subject to FastAPI `/v1/intent/classify`, FastAPI returns a routing decision `{ route, confidence }` within 80ms p95. Gateway uses this as advisory input alongside its hard-coded fallback route.
- **Maps to:** `fastapi_rag_service_spec.md §W4 — User Intent Classification` (data contracts, acceptance gates)
- **Explicitly out of scope:**
  - FastAPI becoming the primary router — Gateway remains the source of truth for route table
  - Training a custom classification model — uses LLM-as-classifier (gpt-4o-mini or local quantized)
  - W1/W2/W3 workloads — already live

---

## Layer Changes

| Layer | Service | Change |
|-------|---------|--------|
| Application | `src/FastAPIService/app/api/v1/intent.py` | New — `POST /v1/intent/classify` endpoint |
| Application | `src/FastAPIService/app/rag/intent_classifier.py` | New — lightweight LLM-as-classifier with confidence scoring |
| Application | `src/Gateway/Middleware/IntentAdvisoryMiddleware.cs` | Modify — call FastAPI for advisory, merge with hard-coded fallback |

---

## Data Contracts

Adopted by reference from `fastapi_rag_service_spec.md §W4 — User Intent Classification` (rev 2026-06-16).

**Gateway → FastAPI:**
```json
POST /v1/intent/classify
{
  "body": "raw request body text",
  "user_id": "uuid from JWT sub",
  "available_routes": ["events.ingest", "events.validate", "support.create", "assistant.ask"]
}
```

**FastAPI → Gateway response (≤ 80ms p95):**
```json
{
  "route": "events.ingest",
  "confidence": 0.87
}
```

**Fallback contract:** Gateway MUST have a hard-coded fallback route. FastAPI outage → Gateway uses fallback, logs warning, no 500 to client.

---

## Implementation Plan (Commit Units)

### Unit 1 — FastAPI intent classifier endpoint

**Files:**
- `src/FastAPIService/app/api/v1/intent.py` — `POST /v1/intent/classify` route
- `src/FastAPIService/app/rag/intent_classifier.py` — LLM-as-classifier using `gpt-4o-mini` (or env-var model), structured output with confidence, timeout 60s with retry
- `tests/fastapi/test_intent.py` — accuracy + latency tests

**Gate command:** `pytest tests/fastapi/test_intent.py` — must exit 0 with accuracy ≥ 95% and p95 ≤ 80ms.

**Commit message:**
```
feat(fastapi): add W4 intent classifier — lightweight LLM-as-classifier

POST /v1/intent/classify returns {route, confidence} within 80ms p95 using
gpt-4o-mini. FastAPI is advisory only — Gateway holds the route table.
Classifier timeout 60s with tenacity retry. Implements fastapi_rag_service_spec.md §W4.

Day 32 — M5.10 Unit 1 of 2 | Milestone: M5.10 — W4 User Intent Classification
Coverage: accuracy ≥ 95%, p95 ≤ 80ms
Lint: clean
```

### Unit 2 — Gateway IntentAdvisoryMiddleware update

**Files:**
- `src/Gateway/Middleware/IntentAdvisoryMiddleware.cs` — add FastAPI advisory call, merge with hard-coded fallback
- `src/Gateway/Services/IFastAPIClient.cs` — add `ClassifyIntentAsync`
- `src/Gateway/Services/FastAPIClient.cs` — implement `ClassifyIntentAsync`
- `tests/Kendo.Tests/Integration/FastAPIWorkloadTests.cs` — add W4 integration tests

**Gate command:** `dotnet test tests/Kendo.Tests --filter "Category=FastAPIW4"` — must exit 0. Verify fallback routing works when FastAPI returns 503.

**Commit message:**
```
feat(gateway): integrate W4 intent advisory into IntentAdvisoryMiddleware

Gateway calls FastAPI /v1/intent/classify for advisory intent routing. FastAPI
outage → Gateway falls back to hard-coded route, logs warning, returns 200 to
client. FastAPI is never the single point of failure for routing.

Day 32 — M5.10 Unit 2 of 2 | Milestone: M5.10 — W4 User Intent Classification
Coverage: 100% (including fast-failover test)
Lint: clean
```

---

## Success Checklist

Maps 1:1 to `fastapi_rag_service_spec.md §W4 acceptance gate`:

| # | Criterion | Maps to |
|---|-----------|---------|
| 1 | Classification latency p95 ≤ 80ms | M5.10 acceptance |
| 2 | Routing accuracy ≥ 95% on the intent test set | M5.10 acceptance |
| 3 | Gateway has hard-coded fallback route; FastAPI outage degrades to "previous behavior" not 500 | M5.10 acceptance |
| 4 | Classifier model configurable via env var (gpt-4o-mini default, local quantized model path optional) | Operational flexibility |
| 5 | Confidence score returned with every classification | Auditability |

---

## Resilience Mandate

- **Critical design constraint:** W4 MUST NOT be a single point of failure. The Gateway always has a hard-coded fallback route. If FastAPI is down or the intent call times out, the Gateway proceeds with the fallback route and logs the incident.
- `ClassifyIntentAsync` uses the existing `IFastAPIClient` Polly pipeline with a tighter timeout (2s vs 30s for RAG) — the intent call sits on the request hot path and must not add latency.
- The FastAPI classifier endpoint has its own tenacity retry (2 retries, 100ms backoff) before returning a fallback routing decision rather than a 500.
- Health endpoint exempt from classification.

---

## Depends on

- `docs/architecture/fastapi_rag_service_spec.md §W4` — data contracts, acceptance gates
- `src/Gateway/Middleware/IntentAdvisoryMiddleware.cs` — existing middleware, modified
- `src/Gateway/Services/IFastAPIClient.cs` — extended with `ClassifyIntentAsync`
