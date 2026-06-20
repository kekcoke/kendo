# Architecture Spec — Day 23: Gateway → FastAPI Client Integration

> **Milestone:** M5.4 — Gateway integration: typed client + RAG routes
> **Roadmap phase:** 05 — AI/Vector Service (FastAPI)
> **Status:** 🟡 Generated — awaiting Phase 2 implementation
> **Spec date:** 2026-06-19

---

## Milestone Scope

- **Milestone:** M5.4 — Gateway integration: typed `IFastAPIClient` with `/api/rag/{sync,stream}` routes
- **Roadmap phase:** 05
- **Components touched:**
  - `src/Shared/Http/IFastAPIClient.cs` — add `RagQueryAsync`, `RagQueryStreamAsync` + DTOs
  - `src/Shared/Http/FastAPIClient.cs` — implement new methods using existing Polly pipeline
  - `src/Gateway/Controllers/RagController.cs` — add `/api/rag/query` + `/api/rag/stream` routes
  - `tests/Kendo.Tests/Integration/FastAPIIntegrationTests.cs` — new test file
  - `ops/runbooks/day_23_runbook.md` — new runbook
- **Explicitly out of scope:**
  - M5.5 (pybreaker/tenacity resilience in FastAPI) — deferred
  - M5.6 (observability + chaos) — deferred
  - All workload sub-milestones (W1–W8)

---

## Layer Changes

| Layer | Service | Change |
|-------|---------|--------|
| Shared | `Kendo.Shared.Http` | Add `RagQueryRequest`/`Result`/`StreamChunk` DTOs, `RagQueryAsync` + `RagQueryStreamAsync` |
| Application | `Gateway` | Add `GET /api/rag/query` and `GET /api/rag/stream` routes to `RagController` |
| Tests | `Kendo.Tests` | New `FastAPIIntegrationTests.cs` with Category=FastAPI |

---

## Data Contracts

### `GET /api/rag/query`
**Gateway route** → proxies to `POST /v1/rag/query` on FastAPI.

**Request (query params):** `q` (string, required), `top_k` (int, default 5)
**Response — 200 OK:**
```json
{
  "answer": "string",
  "contexts": [
    { "id": "uuid", "text": "string", "score": 0.87, "source": "string" }
  ],
  "traceId": "string"
}
```
**Error:** 503 RFC 7807 when FastAPI circuit breaker open; 400 on missing `q`.

### `GET /api/rag/stream`
**Gateway route** → proxies to `POST /v1/rag/stream` on FastAPI → SSE `text/event-stream`.

Same request params as query. Response is SSE chunks of type `token`, `done`, or `error`.

---

## Implementation Plan (Commit Units)

### Unit 1 — Interface + Client: RAG query/stream methods
- **Files:** `src/Shared/Http/IFastAPIClient.cs`, `src/Shared/Http/FastAPIClient.cs`
- **Changes:**
  - Add `RagQueryRequest`, `RagQueryResult`, `RagStreamChunk` DTOs
  - Add `Task<RagQueryResult> RagQueryAsync(RagQueryRequest, CancellationToken)` to interface + implementation
  - Add `IAsyncEnumerable<RagStreamChunk> RagQueryStreamAsync(RagQueryRequest, CancellationToken)` to interface + implementation
  - Reuse existing `ExecuteWithPipelineAsync` and `JsonOptions`
- **Gate command:** `dotnet build src/Kendo.slnx`
- **Commit message:** `feat(shared): add RagQueryAsync and RagQueryStreamAsync to IFastAPIClient`

### Unit 2 — Gateway RAG routes
- **Files:** `src/Gateway/Controllers/RagController.cs`
- **Changes:** Add two new actions — `RagQuery` (GET), `RagQueryStream` (GET)
- **Gate command:** `dotnet build src/Kendo.slnx`
- **Commit message:** `feat(gateway): add /api/rag/query and /api/rag/stream routes`

### Unit 3 — Tests + Runbook
- **Files:** `tests/Kendo.Tests/Integration/FastAPIIntegrationTests.cs`, `ops/runbooks/day_23_runbook.md`
- **Changes:** Integration tests for valid query, FastAPI-down scenario, SSE stream
- **Gate command:** `dotnet test tests/Kendo.Tests --filter "Category=FastAPI"`
- **Commit message:** `feat(tests): add FastAPI integration tests with Category=FastAPI`

---

## Success Checklist

| # | Criterion | Maps to |
|---|-----------|---------|
| 1 | `IFastAPIClient.RagQueryAsync` returns structured `RagQueryResult` with answer + contexts | M5.4 |
| 2 | `IFastAPIClient.RagQueryStreamAsync` yields SSE chunks with token, done, error types | M5.4 |
| 3 | `GET /api/rag/query` returns 200 with answer + contexts | M5.4 |
| 4 | `GET /api/rag/query?q=` (empty) returns 400 | M5.4 |
| 5 | `GET /api/rag/stream` returns `text/event-stream` with SSE chunks | M5.4 |
| 6 | FastAPI circuit breaker open → 503 RFC 7807 | M5.4+M5.5 |
| 7 | All Phase 01–22 acceptance criteria still pass (no .NET regression) | Inherited |

---

## Resilience Mandate

- **All FastAPI calls** reuse the existing Polly v8 pipeline in `FastAPIClient`: timeout (30s) → retry (3, exp backoff + jitter) → circuit breaker (3 failures, 30s break).
- **No new resilience config.** The `Kendo__FastApi__*` env vars already exist in docker-compose.
- **No new DI registration.** `AddKendoFastApiClient()` already registers `IFastAPIClient`.
- **Gateway JWT auth** is inherited via `[Authorize]` on the controller — no new auth logic.
