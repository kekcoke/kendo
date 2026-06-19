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

## Workload Catalog — what FastAPI is actually for

The spec was originally framed around a single workload (synchronous RAG over event data).
After a PM-level review of the existing platform, **eight distinct workloads** warrant
FastAPI offload. They are cataloged below so each can be specced, prioritized, and
gated independently inside Phase 05.

Each workload section defines: trigger, data contract, why-FastAPI (vs .NET), acceptance
gates, and dependencies. The full **M5.1–M5.6** sequence in §Implementation Plan covers
the **foundation** (scaffold, pgvector, RAG pipeline, resilience, streaming, gateway
integration). Individual workloads layer on top of that foundation and are tracked
as **M5.7+** sub-milestones so a single workload's slip does not block the platform
going live.

> **Project identity:** this repo houses the **Kendo** platform (resilient .NET
> microservices) being built by the **Infraspekt** AI-native factory (the orchestration
> loop in `.ai/`). Workload names below are grounded in Kendo's domain signals —
> `UserService` (the only production service), the `UserCreatedEvent` already flowing
> through Rebus, and the Event Scheduling RAG vision from `docs/flow.md`.

---

## Reconciled Cross-Cutting Decisions (Day 17 PM review)

After a reconciliatory audit of Gateway, Service Bus, Worker, and UserService, five
blocking decisions were resolved before this spec and its sibling specs (day_17
Gateway, day_18 Service Bus, day_19 Worker, day_20 UserService) were authored. They
are recorded here so the rest of the spec — and every dependent spec — has a
single source of truth.

### CCD-1 — Embedding model + dimension (split: local vs cloud)

| Environment | Model | Dimensions | Notes |
|---|---|---|---|
| **Local dev / CI** | `BGE-large-en-v1.5` (self-hosted via `sentence-transformers`) | **1024** | No Azure dependency, no API key, deterministic on CPU; slower p95 (~150ms / 1k tokens) but acceptable for dev. Hosted as a sidecar in docker-compose; FastAPI hits it over HTTP. |
| **Cloud / staging / prod** | Azure OpenAI `text-embedding-3-small` | **1536** | Production latency p95 ~40ms, swap-friendly via `app/llm/embeddings.py` single seam. |

**Implication for the schema:** `vector` columns in `UserService` MUST be `vector(1024)`
for local-dev parity. In cloud, a separate `event_embeddings_cloud (vector(1536))`
table is used and FastAPI picks the embedding client at startup based on
`KENDO_ENV` (or equivalent). **No mixing**: a row written in cloud is never read in
local; a local reindex wipes the cloud table on first cloud-only run, and vice
versa, enforced by a startup check that asserts `KENDO_EMBEDDING_MODEL` matches the
active table.

### CCD-2 — JWT strategy (RS256, Gateway-served JWKS)

- Algorithm: **RS256**.
- Signing: the **Gateway** is the single trust root. It generates an
  `RSA-2048` keypair on first boot (private key in `KENDO__JWT__PRIVATE_KEY_PATH`,
  default `secrets/jwt-private.pem`), persists the public key at
  `/.well-known/jwks.json` (RFC 7517), and rotates the keypair on the schedule
  defined in `KENDO__JWT__ROTATION_DAYS` (default 90 days, two-key overlap window).
- **FastAPI** fetches the JWKS once on startup (with a 1-hour in-memory cache
  refreshed on `kid` miss) and validates inbound `Authorization: Bearer <jwt>`
  locally. No shared secret in env vars. No external IdP dependency.
- **Audience:** `kendo.api`. **Issuer:** `https://gateway.local/.well-known/jwks.json`.
  Both are configurable but default to the dev-friendly values.
- The full spec lives in **`docs/architecture/day_17_spec.md`** (Gateway) — this
  spec only consumes the contract.

### CCD-3 — `EmbeddingAdminController` auth (JWT scope claim, with A & B planned)

- **Now (C):** `UserService` exposes `POST /internal/embeddings` and
  `POST /internal/events/{id}/reindex`. The endpoint requires a Gateway-issued JWT
  with the **`admin:writes`** scope claim. FastAPI's `IFastAPIClient` to
  `UserService` mints a short-lived (≤ 60s) service JWT signed with the Gateway's
  private key, with `scope: "admin:writes"`, and presents it on every write.
- **`UserService`'s `EmbeddingAdminController`** validates the JWT using the same
  JWKS as the Gateway, checks the `admin:writes` scope, and rejects with RFC 7807
  `403` on scope failure.
- **Future A (mTLS):** a future spec (post-M5.11) will add an mTLS path as a
  second, mutually-exclusive auth mode. The contract is the same JWT-issued `POST
  /internal/embeddings`; only the auth middleware changes.
- **Future B (service token):** a future spec (post-M5.11) will add a static
  `KENDO_INTERNAL_ADMIN_TOKEN` path for emergency break-glass scenarios. The
  contract remains the same; the auth middleware falls back to constant-time
  string compare on the static token when present.
- **Defense-in-depth is preserved:** the `fastapi_ro` PostgreSQL role from M5.3 is
  unchanged and still rejects any write from FastAPI's connection string. The
  admin endpoint is a separate auth path that runs as the `userservice_writer`
  role, granted only the minimum writes (UPDATE/INSERT on `event_embeddings` and
  `events`).

### CCD-4 — AI event queue topology (dedicated `kendo-events-ai`)

- The existing **`kendo-events`** queue keeps the user-lifecycle flow (UserCreated
  → UserCreatedEvent → UserCreatedEventHandler → DLQ on failure). No change.
- A new **`kendo-events-ai`** queue carries the AI-mediated events: EventIngested,
  EventValidated, UserEmbeddingUpdated, NotificationRequested. It is provisioned
  in `KendoRebusConfiguration` as a separate consumer, with its own worker count
  (default 2, parallelism 5), its own DLQ `kendo-events-ai/$DeadLetterQueue`, and
  its own `DlqDepthMonitor` (alert threshold default 5).
- A new **`kendo-events-ai-producer`** one-way client is registered on the
  Gateway (for W1, W4 → emit `EventIngested` from inbound requests) and on
  `UserService` (for W3/W5 → emit `UserEmbeddingUpdated` after a successful
  reindex). The Worker registers as a **consumer** for both queues.
- **Operational consequence:** chaos tests for `kendo-events` and `kendo-events-ai`
  are independent. A poison message on one queue cannot starve the other.

### CCD-5 — Authoring scope (this batch)

- **This turn** authors: this FastAPI spec update (resolves CCD-1, CCD-2, CCD-3,
  CCD-4), plus four sibling specs:
  - `docs/architecture/day_17_spec.md` — Gateway: AI integration + JWT auth
  - `docs/architecture/day_18_spec.md` — Service Bus: AI event contracts
  - `docs/architecture/day_19_spec.md` — Worker: AI handlers
  - `docs/architecture/day_20_spec.md` — UserService: Event domain + embeddings
- These four specs are the **prerequisites** the FastAPI implementation depends
  on. The M5.7+ workloads cannot ship to production until all four are merged
  to `develop`.

### W1 — Event Ingestion RAG (the workload `flow.md` already names)

- **Trigger:** `POST /api/events/ingest` on the Gateway — user submits a free-form
  event description ("Bob's surprise birthday, Sat 7pm, 12 guests, no nuts").
- **Data contract:** Gateway forwards the raw text to FastAPI. FastAPI chunks, embeds,
  retrieves the top-K most similar past events from pgvector, and calls Azure OpenAI
  to produce a structured `Event` JSON (name, date/time, location, headcount, dietary,
  etc.). Gateway receives the structured payload, validates it, and writes to
  `UserService` via EF Core exactly like `UserCreatedEvent` does today.
- **Why FastAPI (not .NET):** heavy LLM call (5–20s p95); benefits from SSE streaming
  to surface partial results; the LangChain ecosystem has first-class text splitters,
  retrievers, and output parsers that the C# SDK does not match.
- **Acceptance gate:** p95 latency ≤ 8s; grounded answer rate ≥ 90% on a held-out test
  set (DeepEval/Ragas); embedding model parity with `UserService`'s ingestion path
  enforced by a contract test.
- **Depends on:** M5.1 (scaffold), M5.2 (RAG pipeline), M5.3 (pgvector read), M5.5
  (resilience), M5.6 (observability). M5.4 (Gateway routing) is the trigger.
- **Sub-milestone ID:** **M5.7**.

### W2 — Event Conflict & Schedule Reasoning

- **Trigger:** `POST /api/events/{id}/validate` — called by Gateway before persisting a
  newly-structured event from W1.
- **Data contract:** FastAPI receives the candidate `Event` JSON + the requesting
  user's ID. It queries pgvector for the user's recent events, asks the LLM to perform
  multi-step reasoning (does this conflict with an existing booking? does the time
  collide with a recurring event? is the headcount plausible given the venue?), and
  returns a `ValidationResult` (`{ ok: bool, conflicts: [...], suggestions: [...] }`).
- **Why FastAPI (not .NET):** multi-step reasoning + tool-use (date math, venue lookup)
  is what LangChain agents are designed for; .NET Semantic Kernel can do it but the
  Python tooling (`langgraph`, function-calling schemas, structured output parsers) is
  several iterations ahead.
- **Acceptance gate:** zero false-negatives on the conflict regression test set (a
  conflict is never missed); false-positive rate ≤ 5%; reasoning trace returned with
  every response for auditability.
- **Depends on:** W1 (so pgvector has event embeddings to reason over), M5.4.
- **Sub-milestone ID:** **M5.8**.

### W3 — User Profile Semantic Search

- **Trigger:** `GET /api/users/search?q=...` — admin/support tool to find users by
  intent ("users who joined in the last 30 days and have an unverified email") rather
  than exact field match.
- **Data contract:** FastAPI embeds the query, runs hybrid search (pgvector cosine +
  Postgres full-text on user attributes), returns the top-N matching user IDs with a
  relevance score. The Gateway resolves IDs → user records via `UserService` and
  returns the full profile list.
- **Why FastAPI (not .NET):** the user table is currently small but pgvector cosine +
  BM25 hybrid scoring is one `langchain.retrievers.EnsembleRetriever` call in Python;
  rebuilding it in C# is a 2-week project for no platform benefit.
- **Acceptance gate:** precision@10 ≥ 0.85 on a labeled query set; hybrid search
  latency ≤ 300ms p95.
- **Depends on:** M5.2 (RAG pipeline), M5.3 (pgvector), a new
  `UserService/Migrations/...AddUserEmbeddingColumn.cs` migration (M5.3's migration is
  extended, not replaced).
- **Sub-milestone ID:** **M5.9**.

### W4 — User Intent Classification / Gateway Routing

- **Trigger:** every authenticated request that hits an ambiguous Gateway route
  (e.g., `POST /api/messages` where the body could be an event, a support request, a
  status update, etc.).
- **Data contract:** FastAPI receives the request body + JWT subject, classifies
  intent in ≤ 80ms using a small/fast LLM (e.g., `gpt-4o-mini` or a local
  quantized model), and returns a routing decision
  (`{ route: "events.ingest" | "support.create" | "..." , confidence: float }`).
- **Why FastAPI (not .NET):** the classification is a single LLM call, must be
  edge-fast, and lives on the request hot path. FastAPI's async + `httpx` keep
  per-request overhead minimal; doing this in .NET would force a new in-process
  SDK dependency for marginal benefit. The .NET side stays the source of truth for
  the route table — FastAPI is consulted as an *advisor*, not the router.
- **Acceptance gate:** classification latency p95 ≤ 80ms; routing accuracy ≥ 95% on
  the intent test set; the Gateway always has a hard-coded fallback route, so a
  FastAPI outage degrades to "previous behavior" not a 500.
- **Depends on:** M5.1 (scaffold), M5.4 (Gateway integration), M5.5 (resilience).
- **Sub-milestone ID:** **M5.10**.

### W5 — Embeddings Backfill & Re-indexing (batch)

- **Trigger:** cron-driven (`apscheduler` in the FastAPI process) or one-shot CLI
  (`python -m app.jobs.reindex --since 2026-01-01`).
- **Data contract:** FastAPI reads rows from `UserService` (via internal API — no
  direct DB write, preserving the read-only `fastapi_ro` boundary), computes
  embeddings in batches of 64, writes them back to a new `event_embeddings` table
  owned by `UserService` (write performed by `UserService` via an internal
  admin endpoint, not by FastAPI — defense in depth).
- **Why FastAPI (not .NET):** batch embedding jobs are a Python strength (`tenacity`
  retry, `asyncio.gather` parallelism, `numpy`-backed batching). The .NET stack would
  need a Python interop or a third-party SDK to do the same job at the same cost.
- **Acceptance gate:** 10k events re-embedded in ≤ 5 minutes on a 2-core container;
  idempotent (re-running produces no duplicates); resumable (process death mid-run
  resumes from the last checkpoint, not from zero).
- **Depends on:** M5.2, M5.3. The `event_embeddings` table is a new
  `UserService` migration.
- **Sub-milestone ID:** **M5.11**.

### W6 — Document Q&A / Onboarding Assistant (internal RAG)

- **Trigger:** `POST /api/assistant/ask` — internal tool used by the Infraspekt
  agents (Architect, Developer, QA, DevOps) and by new engineer onboarding.
- **Data contract:** FastAPI ingests the corpus: every `docs/architecture/day_*.md`,
  every `ops/runbooks/*.md`, every `templates/skills/*.md`, the `changelog/`
  directory, and `.ai/orchestration.md`. Index is rebuilt whenever those files
  change (file-watcher → debounced reindex, ≤ 1 rebuild per 5 minutes). Queries
  return cited answers (source file + line range) so the orchestrator can verify
  the answer against the spec, not just trust the LLM.
- **Why FastAPI (not .NET):** internal tool, not on the public hot path; the Python
  ecosystem for citations and source-grounded RAG (`langchain`'s citation support,
  `llama-index`'s source nodes) is materially better than the C# alternatives.
- **Acceptance gate:** citation accuracy ≥ 95% (the cited file + line range actually
  contains the answer); answer faithfulness ≥ 0.9 on a held-out QA set; latency
  p95 ≤ 4s.
- **Depends on:** M5.1, M5.2, M5.3, M5.5, M5.6. Ingestion is a one-time CLI job plus
  a file-watcher sidecar.
- **Sub-milestone ID:** **M5.12**.

### W7 — Event Notification Summarization (streaming into Worker)

- **Trigger:** emitted by the `Worker` after it processes a `UserCreatedEvent` or
  any future `EventCreatedEvent` — instead of sending a raw templated email, the
  Worker calls FastAPI to generate a personalized summary.
- **Data contract:** Worker calls `POST /v1/notifications/summarize` (SSE) with
  `{ event_id, user_id, template_id, tone }`. FastAPI streams back the rendered
  notification body. Worker hands the result to whatever delivery channel exists
  (email, push, in-app).
- **Why FastAPI (not .NET):** SSE streaming, prompt-template versioning, tone
  control, and A/B-testable prompt variants are all first-class concerns in the
  Python LLM ecosystem. The C# side stays the integrator (Worker calls FastAPI);
  FastAPI is the rendering engine.
- **Acceptance gate:** end-to-end notification latency p95 ≤ 6s; per-tenant tone
  control respected; prompt-version stored on the produced notification for audit;
  full RFC 7807 on failure so the Worker can DLQ the message rather than retry-loop.
- **Depends on:** M5.4 (Gateway integration is irrelevant here — Worker calls
  FastAPI directly via a new `IFastAPIClient` in `Kendo.Shared`), M5.5, M5.6.
- **Sub-milestone ID:** **M5.13**.

### W8 — Evaluation & Regression Gate for LLM Outputs

- **Trigger:** runs in CI on every PR that touches `src/FastAPIService/` or
  `docs/architecture/fastapi_rag_service_spec.md`; also runs nightly against the
  `develop` branch.
- **Data contract:** a pytest suite using **DeepEval** (hallucination, faithfulness,
  answer relevancy) and **Ragas** (context precision/recall) evaluates every
  FastAPI endpoint against a frozen golden dataset stored in
  `tests/fastapi/eval/datasets/`. Results are uploaded as a CI artifact and fail
  the build if any metric regresses by > 5% versus the `main` branch baseline.
- **Why FastAPI (not .NET):** DeepEval, Ragas, and the entire LLM-evaluation
  ecosystem are Python-first. The M3.3 chaos suite already runs xUnit; W8 is the
  LLM analog of that suite and naturally lives in Python.
- **Acceptance gate:** CI fails on any metric regression > 5%; the eval dataset is
  versioned in git (so regressions are reproducible); the suite completes in ≤ 10
  minutes in CI (gated by a `pytest --co` to keep the fast suite fast).
- **Depends on:** at least one of W1/W2/W3/W6 being live (each adds datasets). W8
  itself is **infrastructure** that ships before its first consumer.
- **Sub-milestone ID:** **M5.14**.

### Workload prioritization matrix

| # | Workload | User value | Build cost | Priority | First shippable in |
|---|---|---|---|---|---|
| W1 | Event Ingestion RAG | High (unlocks Event Scheduling) | M | **P0** | M5.7 |
| W2 | Event Conflict & Schedule Reasoning | High (correctness gate) | M | **P0** | M5.8 |
| W3 | User Profile Semantic Search | Medium | S | P1 | M5.9 |
| W4 | User Intent Classification | Medium | S | P1 | M5.10 |
| W5 | Embeddings Backfill & Re-indexing | Low (operational) | S | P1 | M5.11 |
| W6 | Document Q&A / Onboarding Assistant | Medium (internal tool) | S | P2 | M5.12 |
| W7 | Event Notification Summarization | Medium | S | P2 | M5.13 |
| W8 | Evaluation & Regression Gate | High (protects all the above) | S | **P0** | M5.14 (ship before W1 ships to prod) |

> **Dependency rule:** W8 ships to CI before W1 ships to production. The eval
> dataset for W8 starts empty (a 0% baseline) and grows with each workload that
> goes live. This guarantees we never deploy an LLM change that we cannot
> measure.

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
- **Workload sequencing (W1–W8):** the prioritization matrix in the Workload Catalog
  ships P0 workloads (W1, W2, W8) before P1/P2, but the orchestrator's day-by-day
  cadence may want to alternate (e.g. one foundation day, one W8-shipping day, one
  W1-shipping day) to keep the eval gate honest. Confirm preferred cadence before
  the first M5.x day.
- **W3 — User embedding column:** W3 needs a new `UserService` migration adding an
  embedding column to the `users` table (or a sidecar `user_embeddings` table).
  Confirm naming convention (sidecar table is recommended to keep the
  `kendo_users.users` table unchanged for downstream consumers).
- **W4 — Routing safety:** W4 introduces FastAPI as an *advisor* on the request hot
  path. The Gateway MUST have a hard-coded fallback route, and W4 MUST NOT be a
  single point of failure. Confirm the fallback routing policy is documented in
  the Gateway's resilience spec (currently a carry-forward).
- **W5 — Defense-in-depth writes:** W5's design pushes the actual write through
  `UserService` via an internal admin endpoint rather than letting FastAPI write
  directly, preserving the `fastapi_ro` role's read-only contract. This adds a
  new `UserService` admin endpoint that needs its own auth model (mTLS? service
  token? signed request?). Confirm auth choice before M5.11 starts.
- **W6 — Corpus scope:** W6 ingests `docs/architecture/`, `ops/runbooks/`,
  `templates/skills/`, `changelog/`, and `.ai/orchestration.md`. Confirm whether
  `.ai/current_state.md` and the `templates/agents/` personas are also in scope
  (they may contain operational details the agents should not be able to query
  adversarially).
- **W7 — Worker-side client:** W7 needs a new `IFastAPIClient` registered in
  `Kendo.Shared` so the `Worker` can call FastAPI directly without going through
  the Gateway. This is the **first non-Gateway caller** of FastAPI. Confirm
  whether the Worker should reuse the Gateway's Polly policies (recommended) or
  define its own.
- **W8 — Dataset sourcing:** W8 needs a frozen golden dataset per workload.
  For W1, the seed dataset can be synthesized from the `Event` JSON shapes in
  the planned `EventCreatedEvent` schema. For W6, the seed dataset is the
  spec itself (questions answerable from `platform_roadmap.md` and
  `orchestration.md`). Confirm acceptable dataset provenance policy.

---

## Change Log

| Date | Author | Change |
|---|---|---|
| 2026-06-16 | Platform Architect (spec) | Initial spec — drives Phase 05 implementation (M5.1–M5.6 foundation) |
| 2026-06-16 | Platform Architect (PM review) | Added Workload Catalog (W1–W8) and M5.7–M5.14 sub-milestones. Spec now covers 8 distinct workloads (P0: W1, W2, W8 · P1: W3, W4, W5 · P2: W6, W7) with per-workload data contracts, acceptance gates, and dependency maps. Roadmap Phase 05 and `.ai/current_state.md` updated to match. |
