# Architecture Spec — Day 22: LangChain RAG Pipeline + pgvector Read Integration

> **Milestone:** M5.2 + M5.3 — LangChain RAG pipeline + read-only pgvector role
> **Roadmap phase:** 05 — AI/Vector Service (FastAPI)
> **Status:** 🟡 Generated — awaiting Phase 2 implementation
> **Adopts:** `docs/architecture/fastapi_rag_service_spec.md` (pre-authored spec, Units 2-3)
> **Spec date:** 2026-06-19

---

## Milestone Scope

- **Milestones:** M5.2 (LangChain RAG pipeline: `/v1/rag/query` sync + `/v1/rag/stream` SSE) + M5.3 (read-only pgvector integration with `fastapi_ro` role)
- **Roadmap phase:** 05 — AI/Vector Service (FastAPI)
- **Components touched:**
  - `src/FastAPIService/` — Python code: pgvector connection, embeddings repository, LangChain chain, prompts, RAG routes, JWT auth upgrade, readiness wiring
  - `src/UserService/Data/Migrations/` — new `AddFastAPIConnectionPool` migration (or verify `fastapi_ro` role already exists from Day 20 M0.6)
  - `.env.example` — `FASTAPI__VECTOR__READ_DSN`, `FASTAPI__LLM__*` env vars
  - `docker-compose.yml` — optional: `FASTAPI__*` env vars for LLM/pgvector
- **Explicitly out of scope:**
  - M5.4 (Gateway → FastAPI client integration) — deferred
  - M5.5 (Resilience: pybreaker/tenacity) — deferred
  - M5.6 (Observability: OpenTelemetry, chaos tests) — deferred
  - All M5.7+ workload sub-milestones (W1–W8)
  - Any Phase 01–04 work (already complete)

---

## Layer Changes

| Layer | Service | Change |
|-------|---------|--------|
| Application | `FastAPIService` | New `app/db.py` — asyncpg pool with `fastapi_ro` role |
| Application | `FastAPIService` | New `app/repositories/embeddings.py` — cosine similarity search |
| Application | `FastAPIService` | New `app/rag/chain.py` — LangChain `RetrievalQA` chain |
| Application | `FastAPIService` | New `app/rag/prompts.py` — versioned prompt templates |
| Application | `FastAPIService` | New `app/api/v1/rag.py` — `POST /v1/rag/query` endpoint |
| Application | `FastAPIService` | New `app/api/v1/rag_stream.py` — `POST /v1/rag/stream` SSE endpoint |
| Application | `FastAPIService` | Updated `app/middleware/auth.py` — real JWKS validation (replaces scaffold accept-any-token) |
| Application | `FastAPIService` | Updated `app/main.py` — wire readiness to pgvector + LangChain checks |
| Data | `UserService` | If not already present: `fastapi_ro` role + SELECT grants migration |
| Configuration | `.env.example` | `FASTAPI__VECTOR__READ_DSN`, `FASTAPI__LLM__*`, `FASTAPI__AZURE_OPENAI__*` |
| Configuration | `docker-compose.yml` | Wire `FASTAPI__*` env vars to fastapi service |

---

## Data Contracts

### `POST /v1/rag/query` — Synchronous RAG query (non-streaming)

All endpoints require `Authorization: Bearer <jwt>`. JWT is validated locally via JWKS.

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
  "trace_id": "string"
}
```

**Error responses** — RFC 7807:
- `400` malformed request
- `401` invalid/missing JWT
- `422` LangChain validation error
- `503` pgvector or Azure OpenAI unreachable

### `POST /v1/rag/stream` — Server-Sent Events streaming RAG

Returns `text/event-stream` of token chunks. Each event includes a `traceparent` segment.

### `GET /health/ready` — Readiness probe (updated)

Returns `200 OK` only when pgvector is reachable AND LangChain pipeline is loaded.
Returns `503 RFC 7807` with failing dependency named.

### Read-only pgvector contract

FastAPI connects to PostgreSQL using the `fastapi_ro` role (SELECT-only grants):

```sql
-- Already provisioned by Day 20 migration AddFastAPIReadOnlyRole
-- FastAPI connects with: FASTAPI__VECTOR__READ_DSN
-- SELECT on: events, event_embeddings, user_embeddings, users
```

**No INSERT/UPDATE/DELETE** from FastAPI at any layer — enforced by the role.

---

## Implementation Plan (Commit Units)

### Unit 1 — pgvector read integration + `fastapi_ro` role
- **Files:**
  - `src/FastAPIService/app/db.py` — asyncpg connection pool, read-only DSN, retry on connection fail
  - `src/FastAPIService/app/repositories/__init__.py` — empty package marker
  - `src/FastAPIService/app/repositories/embeddings.py` — `async def similarity_search(embedding, top_k, filters)` using `embedding <=> $1` cosine distance
  - `src/UserService/Data/Migrations/...VerifyFastAPIMigration.cs` — if `fastapi_ro` role exists in DB, skip; otherwise provision it (check existing Day 20 migration)
- **Gate command:** `python -c "from app.db import create_pool; print('ok')"` and `pytest tests/unit/test_embeddings.py -x`
- **Commit message:** `feat(fastapi): add read-only pgvector access via asyncpg + fastapi_ro role`

### Unit 2 — JWT auth: real JWKS validation
- **Files:**
  - `src/FastAPIService/app/middleware/auth.py` — REPLACE scaffold accept-any-token with real RS256 JWKS validation using `httpx` to fetch JWKS, validate `exp`, `aud`, `iss` claims
  - `src/FastAPIService/app/middleware/jwks.py` — JWKS client with caching (1-hour TTL, refresh on `kid` miss)
- **Gate command:** `pytest tests/unit/test_jwt_auth.py -x`
- **Commit message:** `feat(fastapi): upgrade JWT auth to real RS256 JWKS validation`

### Unit 3 — LangChain RAG pipeline (`/v1/rag/query`)
- **Files:**
  - `src/FastAPIService/app/rag/__init__.py` — empty package marker
  - `src/FastAPIService/app/rag/chain.py` — LangChain `RetrievalQA` chain with pgvector retriever
  - `src/FastAPIService/app/rag/prompts.py` — versioned prompt templates
  - `src/FastAPIService/app/rag/embeddings_client.py` — embedding model client (BGE-large-en-v1.5 local / Azure OpenAI)
  - `src/FastAPIService/app/api/v1/__init__.py` — empty package marker
  - `src/FastAPIService/app/api/v1/rag.py` — `POST /v1/rag/query` route
- **Gate command:** `pytest tests/integration/test_rag_query.py -x`
- **Commit message:** `feat(fastapi): implement LangChain RAG /v1/rag/query with pgvector retriever`

### Unit 4 — Streaming RAG (`/v1/rag/stream`)
- **Files:**
  - `src/FastAPIService/app/api/v1/rag_stream.py` — `POST /v1/rag/stream`, `StreamingResponse`, SSE format
  - `src/FastAPIService/app/resilience/streaming_timeout.py` — per-token timeout, partial-response policy
- **Gate command:** `pytest tests/integration/test_rag_stream.py -x`
- **Commit message:** `feat(fastapi): add streaming RAG SSE /v1/rag/stream endpoint`

### Unit 5 — Readiness wiring + env vars + dependency updates
- **Files:**
  - `src/FastAPIService/app/main.py` — wire `/health/ready` to check pgvector + LangChain pipeline
  - `src/FastAPIService/app/routes/health.py` — update readiness to return 503 when dependencies are down
  - `src/FastAPIService/pyproject.toml` — add `langchain`, `langchain-community`, `asyncpg`, `httpx`, `pytest`, `pyjwt`, `cryptography`
  - `.env.example` — `FASTAPI__VECTOR__READ_DSN`, `FASTAPI__LLM__ENDPOINT`, `FASTAPI__LLM__API_KEY`, `FASTAPI__LLM__DEPLOYMENT_NAME`
- **Gate command:** `cd src/FastAPIService && pip install -e . && python -c "from app.main import create_app; print('Dependencies OK')"`
- **Commit message:** `chore(fastapi): add dependencies, readiness wiring, and env vars for M5.2+M5.3`

---

## Success Checklist

| # | Criterion | Maps to |
|---|-----------|---------|
| 1 | FastAPI connects to pgvector using `fastapi_ro` role; `INSERT` fails with permission denied | Roadmap M5.3 |
| 2 | `POST /v1/rag/query` returns structured answer + scored contexts within p95 latency | Roadmap M5.2 |
| 3 | `POST /v1/rag/stream` emits SSE chunks with `traceparent` correlation | Roadmap M5.2 |
| 4 | JWT validation: accepts Gateway-issued token, rejects expired/wrong-audience/missing token | Roadmap M5.1 (upgrade) |
| 5 | `/health/ready` returns 200 when pgvector + LangChain are reachable, 503 otherwise | Roadmap M5.3 |
| 6 | `fastapi_ro` role enforced at DB level (verified by integration test) | Roadmap M5.3 |
| 7 | All Phase 01–04 tests still pass (no .NET regression) | Inherited |
| 8 | Existing Day 21 health endpoints still work: `/health/live` always returns 200 | Regression guard |

---

## Resilience Mandate

- **pgvector:** Connection pool with retry (3 attempts, 1s backoff). Read-only operations only — no circuit breaker in M5.2/M5.3 (deferred to M5.5).
- **Azure OpenAI / LLM:** No circuit breaker yet (deferred to M5.5). Timeout via `httpx.Timeout(30.0)`. Streaming has per-token timeout (30s total, close on timeout).
- **JWT validation:** JWKS fetched at first request, cached for 1 hour. Cache miss triggers re-fetch. Validation failure returns RFC 7807 401 immediately.
- **Health readiness:** Checks pgvector connectivity + LangChain pipeline loaded. Failures return 503 with named dependency.

---

## Dependencies

| Resource | Required? | Status |
|----------|-----------|--------|
| Python 3.12 | Yes | Available |
| FastAPI + Uvicorn | Yes | Already in `pyproject.toml` (Day 21) |
| LangChain (`langchain`, `langchain-community`) | Yes | To be added in Unit 5 |
| `asyncpg` | Yes | To be added in Unit 5 |
| `httpx` | Yes | Already installed in venv |
| `pyjwt` + `cryptography` | Yes | To be added in Unit 5 |
| `pytest` | Yes | To be added in Unit 5 |
| Azure OpenAI endpoint + key | Yes | Provided via env vars |
| `fastapi_ro` PostgreSQL role | Yes | Provisioned by Day 20 migration |
| PostgreSQL + pgvector (running) | Yes | Already in docker-compose |
| Gateway (for JWKS endpoint) | Yes | Already running |
