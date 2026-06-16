# Architecture Spec — FastAPI AI/Vector Service

> **Milestone:** M5.1–M5.6 (Phase 05 rollout)  
> **Roadmap phase:** 05 — AI/Vector Service (FastAPI)  
> **Status:** 🟡 **Planned — not yet implemented**  
> **Architect:** Platform Architect (Orchestrator-delegated)  
> **Spec date:** 2026-06-16

---

## Context — Why this spec exists

The repository's `README.md` references a "Python FastAPI Service" performing LangChain/RAG
work with pgvector. As of this spec, **no FastAPI code exists in `src/`** and the workload
is currently owned by the .NET 10 `UserService` (EF Core + pgvector + Semantic Kernel C#,
per `docs/flow.md` and `docs/architecture/day_02_spec.md`).

This spec defines the **target** FastAPI service so the architecture in the README becomes
buildable, and so Phase 05 of `docs/platform_roadmap.md` has a contract to gate on.

The FastAPI service **co-exists** with the .NET Semantic Kernel path:

- The .NET stack remains primary for **synchronous RAG** (low-latency, in-process, EF Core).
- FastAPI is the **offload target** for heavy / streaming / batch LLM workloads the Gateway
  would rather not block on. The Gateway routes to whichever path is appropriate per
  endpoint.

---

## Milestone Scope

- **Roadmap phase:** 05 — AI/Vector Service (FastAPI)
- **Components touched (planned):**
  - `src/FastAPIService/` — new Python 3.12 + FastAPI service
  - `docker-compose.yml` — add `fastapi` service, health checks, internal network
  - `src/Gateway/` — add typed `HttpClient` calls to `fastapi` with Polly timeout/retry
  - `src/UserService/Data/` — add a read-only pgvector view / function for embedding reads
  - `ops/runbooks/` — new runbook for FastAPI-specific failure modes
- **Explicitly out of scope:**
  - Replacing the .NET Semantic Kernel RAG path
  - Migrating existing RAG endpoints from UserService to FastAPI
  - Multi-tenant auth (FastAPI reuses the Gateway-issued JWT, validated locally)
  - Any Phase 01–03 work (already complete)

---

## Layer Changes

| Layer | Service | Change |
|-------|---------|--------|
| Application | `FastAPIService` (new) | Python 3.12, FastAPI, Uvicorn, LangChain, `pybreaker`, `tenacity`, OpenTelemetry SDK |
| Application | `Gateway` (.NET) | New typed `IFastAPIClient` with Polly timeout + circuit breaker |
| Data | `UserService` | Add read-only `embedding_read` SQL function granting SELECT on pgvector column to a `fastapi_ro` role |
| Infrastructure | Docker Compose | Add `fastapi` service on internal network, port `8000` internal only |
| Configuration | `.env` | New vars: `FASTAPI__AZURE_OPENAI__ENDPOINT`, `FASTAPI__AZURE_OPENAI__API_KEY`, `FASTAPI__VECTOR__READ_DSN` |
| Observability | OpenTelemetry | OTLP exporter configured for traces; traceparent propagated from Gateway |
| Orchestration | `ops/` | New `Dockerfile.fastapi` and `ops/runbooks/fastapi_service.md` |

---

## Data Contracts

### Internal HTTP API — `FastAPIService` (consumed by Gateway)

All endpoints require `Authorization: Bearer <jwt>`. JWT is validated locally with the same
signing key the Gateway uses (RS256, JWKS discoverable via env var). Failures return
**RFC 7807 Problem Details**, matching every other service in the platform.

#### `POST /v1/rag/query` — Synchronous RAG query (non-streaming)

**Request**
```json
{
  "query": "string",
  "top_k": 5,
  "filters": { "source": "string (optional)" }
}
```

**Response — 200 OK**
```json
{
  "answer": "string",
  "contexts": [
    {
      "id": "uuid",
      "text": "string",
      "score": 0.87,
      "source": "string"
    }
  ],
  "trace_id": "string (W3C traceparent)"
}
```

**Error responses** — RFC 7807 with `type`, `title`, `status`, `detail`, `trace_id`:
- `400` malformed request
- `401` invalid / missing JWT
- `422` upstream validation error from LangChain
- `429` rate limit exceeded (`Retry-After` header)
- `503` pgvector or Azure OpenAI circuit breaker open
- `504` upstream LLM timeout

#### `POST /v1/rag/stream` — Server-Sent Events streaming RAG

Returns `text/event-stream` of token chunks. Each event includes a `traceparent` so the
Gateway can correlate the stream back to the originating request span.

#### `GET /health/live` — Liveness probe
Returns `200 OK` if the process is responsive. **No dependency checks.**

#### `GET /health/ready` — Readiness probe
Returns `200 OK` only if all of: pgvector reachable, Azure OpenAI reachable, LangChain
pipeline loaded. Returns `503 RFC 7807` otherwise with the failing dependency named.

---

### Read-only pgvector contract with `UserService`

The FastAPI service **never writes** to pgvector. It reads embeddings produced by
`UserService`'s Semantic Kernel ingestion path.

A dedicated PostgreSQL role `fastapi_ro` is provisioned by a new EF Core migration
(`AddFastAPIReadOnlyRole`) with the following grants:

```sql
-- Applied by UserService migration on startup
CREATE ROLE fastapi_ro LOGIN PASSWORD :'fastapi_ro_pw';
GRANT CONNECT ON DATABASE kendo_users TO fastapi_ro;
GRANT USAGE ON SCHEMA public TO fastapi_ro;
GRANT SELECT ON ALL TABLES IN SCHEMA public TO fastapi_ro;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON TABLES TO fastapi_ro;
```

The FastAPI service connects with `fastapi_ro` credentials via
`FASTAPI__VECTOR__READ_DSN`. **No INSERT / UPDATE / DELETE** are issued by FastAPI at any
layer — this is enforced by the role, not just by code review.

---

### OpenTelemetry trace propagation

The Gateway injects a W3C `traceparent` header into every request to FastAPI. FastAPI
extracts it and creates a child span via `opentelemetry-instrumentation-fastapi`. For
streaming, each SSE event embeds the traceparent so the consumer can stitch spans.

---

## Implementation Plan (Commit Units)

### Unit 1 — Scaffold `FastAPIService` project + Dockerfile
- **Files:**
  - `src/FastAPIService/pyproject.toml` — `fastapi`, `uvicorn[standard]`, `pydantic`, `pydantic-settings`
  - `src/FastAPIService/app/main.py` — `FastAPI()` app factory, health endpoints only
  - `src/FastAPIService/app/config.py` — `Settings` via `pydantic-settings`
  - `src/FastAPIService/Dockerfile` — multi-stage, `python:3.12-slim`, non-root user
  - `docker-compose.yml` — add `fastapi` service, internal port `8000`, health check
- **Gate command:** `docker compose up -d fastapi && curl -fsS localhost:8000/health/live`
- **Commit message:** `feat(fastapi): scaffold FastAPIService with health endpoints`

### Unit 2 — Read-only pgvector integration
- **Files:**
  - `src/FastAPIService/app/db.py` — asyncpg pool, read-only role
  - `src/FastAPIService/app/repositories/embeddings.py` — similarity search by `embedding <=> $1`
  - `src/UserService/Data/Migrations/...AddFastAPIReadOnlyRole.cs` — role + grants
  - `.env.example` — `FASTAPI__VECTOR__READ_DSN`
- **Gate command:** `docker compose exec fastapi python -c "from app.db import pool; ..."` and integration test
- **Commit message:** `feat(fastapi): add read-only pgvector access via fastapi_ro role`

### Unit 3 — LangChain RAG pipeline (`/v1/rag/query`)
- **Files:**
  - `src/FastAPIService/app/rag/chain.py` — LangChain `RetrievalQA` chain
  - `src/FastAPIService/app/rag/prompts.py` — versioned prompt templates
  - `src/FastAPIService/app/api/v1/rag.py` — POST `/v1/rag/query` route
  - `src/FastAPIService/app/middleware/auth.py` — JWT validation (RS256, JWKS)
  - `src/FastAPIService/app/middleware/problem_details.py` — RFC 7807 exception handler
- **Gate command:** `pytest tests/integration/test_rag_query.py`
- **Commit message:** `feat(fastapi): implement LangChain RAG /v1/rag/query endpoint with JWT auth and RFC 7807`

### Unit 4 — Resilience: `pybreaker` + `tenacity` + OpenTelemetry
- **Files:**
  - `src/FastAPIService/app/resilience/circuit_breaker.py` — `pybreaker.CircuitBreaker` per dependency (pgvector, Azure OpenAI)
  - `src/FastAPIService/app/resilience/retry.py` — `tenacity` retry with exponential backoff + jitter, transient-fault predicate
  - `src/FastAPIService/app/observability/tracing.py` — OpenTelemetry SDK + OTLP exporter
  - `src/FastAPIService/app/middleware/trace_propagation.py` — extract `traceparent`, attach to spans
- **Gate command:** `pytest tests/resilience/test_circuit_breaker.py tests/resilience/test_retry.py`
- **Commit message:** `feat(fastapi): add pybreaker + tenacity resilience and OpenTelemetry tracing`

### Unit 5 — Streaming RAG (`/v1/rag/stream`) + rate limiting
- **Files:**
  - `src/FastAPIService/app/api/v1/rag_stream.py` — `StreamingResponse`, SSE format
  - `src/FastAPIService/app/middleware/rate_limit.py` — token-bucket per JWT subject
  - `src/FastAPIService/app/resilience/streaming_timeout.py` — per-token timeout, partial-response policy
- **Gate command:** `pytest tests/integration/test_rag_stream.py`
- **Commit message:** `feat(fastapi): add streaming RAG SSE endpoint and per-JWT rate limiting`

### Unit 6 — Gateway integration + chaos tests
- **Files:**
  - `src/Gateway/Services/FastAPIClient.cs` — `HttpClient` + Polly timeout + circuit breaker
  - `src/Gateway/Controllers/RagController.cs` — route `/api/rag/{sync,stream}` to FastAPIService
  - `tests/Kendo.Tests/Integration/FastAPIChaosTests.cs` — DB-down, Azure-OpenAI-down, slow-stream scenarios
  - `ops/runbooks/fastapi_service.md` — failure-mode playbook
- **Gate command:** `dotnet test tests/Kendo.Tests --filter "Category=FastAPI"` and `docker compose run --rm chaos`
- **Commit message:** `feat(gateway): route RAG traffic to FastAPIService with chaos coverage`

---

## Success Checklist

| # | Criterion | Maps to |
|---|-----------|---------|
| 1 | `docker compose up -d fastapi` starts the container; `/health/live` and `/health/ready` respond correctly | Roadmap M5.1 |
| 2 | FastAPI connects to pgvector using `fastapi_ro` role; `INSERT` from FastAPI fails with permission denied | Roadmap M5.3 |
| 3 | `POST /v1/rag/query` returns a coherent answer + scored contexts from LangChain within p95 latency budget | Roadmap M5.2 |
| 4 | `POST /v1/rag/stream` emits SSE chunks; each event carries a `traceparent` that links to the Gateway span | Roadmap M5.2 + M5.6 |
| 5 | Circuit breaker for Azure OpenAI trips after N consecutive failures; subsequent requests return RFC 7807 `503` (not raw exception) | Roadmap M5.5 + Polly-parity acceptance |
| 6 | Retry policy retries transient DB failures with exponential backoff + jitter; retries are visible in OpenTelemetry traces | Roadmap M5.5 |
| 7 | OpenTelemetry trace IDs appear in every log line for a given request (correlation enforced) | Roadmap M5.5 |
| 8 | Gateway circuit breaker trips when FastAPI is down; clients see RFC 7807 `503` from Gateway, not connection refused | Roadmap M5.5 + M5.6 |
| 9 | Rate limiter returns `429 + Retry-After` above the configured per-JWT threshold | Roadmap M5.6 |
| 10 | Chaos suite runs in CI: DB down, Azure OpenAI down, FastAPI crash, slow stream — all produce structured pass/fail report | Roadmap M5.6 |
| 11 | Runbook `ops/runbooks/fastapi_service.md` covers: Azure OpenAI outage, pgvector read replica failover, FastAPI crash loop, JWKS rotation | Roadmap M5.6 |
| 12 | All Phase 01–03 acceptance criteria still pass after FastAPI integration (regression guard) | Inherited |
| 13 | JWT validation succeeds with a Gateway-issued token; fails closed on expired / wrong-audience / missing token | Security baseline |

---

## Resilience Mandate

This service is the platform's first **non-.NET** workload. The README's "Polly" callout
is .NET-specific; Python equivalents are used here. The mandate below mirrors the
resilience standards established in Phase 01 so the new service is parity-equivalent.

| Concern | .NET equivalent (existing) | Python equivalent (this spec) |
|---|---|---|
| Circuit breaker | `Polly.CircuitBreaker` | `pybreaker.CircuitBreaker` |
| Retry with backoff + jitter | `Polly.Retry` | `tenacity` (`@retry`, `wait_exponential_jitter`) |
| Timeout | `Polly.Timeout` | `tenacity` + `asyncio.wait_for` + `httpx.Timeout` |
| Bulkhead / concurrency | `Polly.Bulkhead` | `asyncio.Semaphore` |
| Health checks | `Microsoft.Extensions.Diagnostics.HealthChecks` | FastAPI route + Docker `HEALTHCHECK` |
| RFC 7807 | `ProblemDetails` middleware | Custom FastAPI exception handler returning `application/problem+json` |
| OpenTelemetry | `OpenTelemetry.Extensions.Hosting` | `opentelemetry-instrumentation-fastapi`, `-asyncpg`, `-httpx` |

**One circuit breaker per external dependency** (pgvector, Azure OpenAI). They trip
independently so a slow LLM does not take down vector reads. Both expose metrics
(`circuit_breaker_state{name=...}` gauge) for the chaos test suite to assert on.

**No raw exceptions cross the network boundary.** Every unhandled exception in a route is
caught by the RFC 7807 handler and emitted as `application/problem+json` with the
`trace_id` field populated.

---

## Dependency Check

| Resource | Required? | Status |
|----------|-----------|--------|
| Python 3.12 | Yes | Available in `python:3.12-slim` Docker base image |
| FastAPI | Yes | `pyproject.toml` dependency in Unit 1 |
| LangChain | Yes | Unit 3; pinned version, model-agnostic interface |
| `pybreaker` | Yes | Unit 4 |
| `tenacity` | Yes | Unit 4 |
| `asyncpg` | Yes | Unit 2 |
| `opentelemetry-*` | Yes | Unit 4 |
| Azure OpenAI endpoint + key | Yes | Provided via `FASTAPI__AZURE_OPENAI__*` env vars |
| `fastapi_ro` PostgreSQL role | Yes | Provisioned by `UserService` migration in Unit 2 |
| `UserService` (existing) | Yes | Owns pgvector writes; FastAPI reads only |
| `Gateway` (existing) | Yes | Caller; receives `IFastAPIClient` integration in Unit 6 |
| NGINX | No | FastAPI is on the internal network, not exposed publicly in M5.1–M5.5 |
| Azure Service Bus | No | FastAPI does not publish events in this phase |
| Redis | No | Optional per-request cache, deferred to a future spec |

---

## Open Questions / Clarifications

- **LLM provider:** Spec assumes **Azure OpenAI** for parity with `UserService`'s Semantic
  Kernel config. If the project standardizes on a different provider later, the
  `app/llm/` module is the single seam to swap.
- **Embedding model parity:** The .NET `UserService` produces embeddings with one model
  (configured per `docs/flow.md`). The FastAPI service must use the **same model** for
  similarity search to remain meaningful. This must be confirmed before M5.2 starts.
- **Streaming partial responses:** If the LLM times out mid-stream, do we close the
  connection (current proposal) or emit a `done` event with a `partial: true` flag and
  RFC 7807 body? Default: close with a structured error event. Confirm UX preference.
- **JWKS endpoint:** The Gateway validates JWTs; the FastAPI service should validate
  locally using a JWKS URL pulled from the Gateway's issuer config. Confirm whether the
  Gateway exposes this URL via an env var or a `/jwks.json` route.
- **Where does `src/FastAPIService/` actually live before Unit 1 lands?** Recommend
  adding a `.gitkeep` + this spec's path in the directory tree *now* (which the README
  update already does) so the planned location is discoverable.
- **Phase 04 collision:** `.ai/current_state.md` lists "Phase 04 (Observability)" as the
  next planned step. The FastAPI work is being tracked as **Phase 05** in
  `docs/platform_roadmap.md` to avoid collision. Confirm the orchestrator accepts this
  numbering.

---

## Change Log

| Date | Author | Change |
|---|---|---|
| 2026-06-16 | Platform Architect (spec) | Initial spec — drives Phase 05 implementation |
