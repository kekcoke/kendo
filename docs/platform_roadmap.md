# Target Platform Resilience Roadmap
> **Orchestration reference.** Loaded by the Entrypoint for the `{{phase_plan}}` section in scope.  
> Each phase defines the milestone markers, acceptance criteria, and architectural components  
> that must exist and function **at phase-end** before the next phase begins.  
> Last updated: 2026-06-16

> **Phase numbering note:** Phases 01–03 are **complete**. **Phase 04 — Pre-FastAPI Reconciliation** 
> has been inserted here to establish the .NET 10 foundations required before the FastAPI 
> service can be integrated. **Phase 05 — AI/Vector Service (FastAPI)** captures the 
> aspirational FastAPI workload referenced in `README.md`. (The previously reserved 
> "Observability & Validation Hardening" is deferred to Phase 06).

---

## Phase 01 — Core APIs & Synchronous Resilience

**Goal:** Establish baseline microservices with robust, observable, synchronous communication.

### Milestone Markers
- **M1.1** Multi-service scaffold: User Service and Background Worker containerized and running locally.
- **M1.2** Database layer: EF Core + pgvector initialized; migrations applied; connection string externalized via environment variable.
- **M1.3** Resilience baseline: all DB and external HTTP calls wrapped in Polly Retry + Circuit Breaker policies.
- **M1.4** Observability foundation: OpenTelemetry traces and structured logs flowing to console/collector.
- **M1.5** API contract: standardized RFC 7807 Problem Details on all 4xx/5xx responses across every service.
- **M1.6** Health checks: `/health/live` and `/health/ready` endpoints present and wired to Docker health checks on every service.

### Acceptance Criteria
- [ ] All REST endpoints return correct HTTP status codes and RFC 7807 error bodies on failure.
- [ ] Circuit breaker trips after the configured threshold; returns a fallback response — not an unhandled exception.
- [ ] Retry policy retries transient DB failures up to N times with exponential backoff; retries are visible in OpenTelemetry traces.
- [ ] OpenTelemetry trace IDs appear in all log lines for a given request (correlation enforced).
- [ ] `docker compose up` starts all services; all `/health/ready` probes pass within startup timeout.
- [ ] xUnit test suite covers circuit breaker trip, circuit breaker reset, and retry exhaustion scenarios with simulated failures.
- [ ] No controller action returns a raw `Exception` message to the client.

### Architectural Components — Existing & Functioning at Phase-End

> Legend: ✅ complete · ❌ failed/blocked · ~ not yet started

| Component | Description | Status |
|---|---|---|
| `.NET 10 Web API (Gateway)` | MVC Controllers, JWT Bearer auth, rate limiting middleware | ✅ (scaffold) |
| `User Service` | MVC Controllers, EF Core, pgvector schema, Data Annotations | ✅ |
| `Background Worker` | .NET Worker Service, scoped DI, hosted lifecycle | ✅ (scaffold) |
| `Polly Circuit Breaker` | Policy applied to all DB clients + external HTTP `HttpClient` | ✅ |
| `Polly Retry` | Exponential backoff, transient-fault predicate, jitter | ✅ |
| `OpenTelemetry` | Traces + structured logs, console exporter, trace-ID in all logs | ✅ |
| `RFC 7807 Problem Details` | Exception-handling middleware, all error responses standardized | ✅ |
| `Health Check Endpoints` | `/health/live` + `/health/ready` per service, wired to Docker | ✅ |
| `pgvector` | Extension initialized, migration applied, embedding column present | ✅ (extension + migration) |
| `Docker Compose` | All services containerized, health checks configured (4 services) | ✅ |

---

## Phase 02 — Asynchronous Decoupling

**Goal:** Introduce message brokers to handle load spikes, prevent timeouts, and guarantee at-least-once delivery.

### Milestone Markers
- **M2.1** Rebus + Azure Service Bus wired: producer and consumer registered in DI; bus starts cleanly.
- **M2.2** First async endpoint: at least one `POST` refactored to return `HTTP 202 Accepted` and publish a domain event.
- **M2.3** Background consumer: message consumer implemented in Worker Service; idempotency key enforced on every handler.
- **M2.4** Dead Letter Queue: DLQ consumer implemented; alert fires when DLQ depth exceeds threshold.
- **M2.5** Outbox pattern: transactional outbox table in EF Core; relay job publishes pending messages atomically.
- **M2.6** Async observability: `traceparent` propagated across producer and consumer; correlated spans visible in traces.

### Acceptance Criteria
- [ ] `POST /[resource]` returns `202 Accepted` with a `Location` header pointing to a polling/status endpoint.
- [ ] Message consumer processes the event and completes the state mutation; original synchronous path is removed.
- [ ] Duplicate messages (identical idempotency key) are silently discarded — no duplicate DB writes.
- [ ] DLQ consumer logs receipt and fires an alert when depth exceeds the configured threshold.
- [ ] Killing the consumer mid-processing and restarting produces exactly-once final state (no partial writes).
- [ ] OpenTelemetry spans link producer trace to consumer trace via `traceparent` header propagation.
- [ ] All Phase 01 acceptance criteria still pass after async refactor.

### Architectural Components — Existing & Functioning at Phase-End

| Component | Description | Status |
|---|---|---|
| `Rebus` | Azure Service Bus transport, DI registration, topology configuration | ✅ |
| `Azure Service Bus` | Queue `kendo-events` provisioned via config; connection string externalized via `Rebus__ConnectionString` env var | ✅ |
| `Async `202` Endpoint` | At least one POST refactored; synchronous path removed | ✅ |
| `Message Consumer` | Worker Service handler, idempotency key enforced per message type | ✅ |
| `Dead Letter Queue Consumer` | DLQ handler, alert-on-threshold, structured log on every receipt | ✅ |
| `Transactional Outbox` | EF Core outbox table + relay hosted service; atomic publish | ✅ |
| `Trace Correlation` | `traceparent` header propagated across Service Bus messages | ✅ |
| *(All Phase 01 components)* | Inherited and still passing all Phase 01 acceptance criteria | ✅ |

---

## Phase 03 — High Availability & Chaos Testing

**Goal:** Ensure the system survives catastrophic component failure without client-visible errors or data loss.

### Milestone Markers
- **M3.1** Load balancer: reverse proxy (NGINX or Azure App Service) routing traffic across ≥ 2 container replicas per service.
- **M3.2** Stateless validation: sticky sessions disabled; any session state externalized to Redis or the database.
- **M3.3** Chaos suite: automated tests simulate DB downtime, service crash, and network partition — all integrated into CI.
- **M3.4** Rate limiting & shedding: request rate limiting enforced at Gateway (`429 + Retry-After`); load shedding returns `503 RFC 7807`.
- **M3.5** Graceful shutdown: all services handle `SIGTERM`; in-flight requests drain before process exits.
- **M3.6** Runbook complete: documented recovery playbook for each failure scenario in `ops/runbooks/`.

### Acceptance Criteria
- [ ] Traffic distributes across ≥ 2 replicas; killing one replica produces zero client-visible errors within health-check TTL.
- [ ] Chaos: DB downtime → circuit breaker trips → fallback response returned; no cascading failure to upstream services.
- [ ] Chaos: Service crash → load balancer marks replica unhealthy within N seconds; traffic rerouted automatically.
- [ ] Chaos: Network partition → Rebus retry + outbox guarantees no message loss.
- [ ] Rate limiter returns `429 Too Many Requests` with `Retry-After` header above the configured threshold.
- [ ] Load shedding returns `503 Service Unavailable` with RFC 7807 body under simulated extreme concurrency.
- [ ] All chaos tests run in CI and produce a structured pass/fail report; pipeline fails on any chaos regression.
- [ ] Runbooks exist for: DB failover, service crash recovery, DLQ drain procedure, horizontal scaling event.
- [ ] All Phase 01 and Phase 02 acceptance criteria still pass.

### Architectural Components — Existing & Functioning at Phase-End

| Component | Description | Status |
|---|---|---|
| `Reverse Proxy / Load Balancer` | NGINX or Azure App Service, ≥ 2 replicas per service, health-check routing | ✅ |
| `Stateless Services` | No sticky sessions; no in-process session state | ✅ |
| `Redis (or equivalent)` | External distributed cache / session store | ✅ |
| `Chaos Test Suite` | xUnit + bash scripts; DB downtime, crash, partition scenarios | ✅ |
| `Rate Limiter` | .NET rate limiting middleware; `429` + `Retry-After` enforced at Gateway | ✅ |
| `Load Shedding` | Concurrency limiter policy; `503` RFC 7807 body under extreme load | ✅ |
| `Graceful Shutdown` | `SIGTERM` handler + drain timeout on all services | ✅ |
| `Ops Runbooks` | `ops/runbooks/` — one file per failure scenario | ✅ |
| *(All Phase 01 + 02 components)* | Inherited and still passing all prior acceptance criteria | ~ |

---

## Phase 04 — Pre-FastAPI Reconciliation

**Goal:** Establish the missing .NET 10 foundations (JWT auth, AI event contracts, messaging topology, and database entities) required before the FastAPI service can be integrated safely.

### Milestone Markers
- **M0.1** Gateway JWT auth: RsaKeyProvider, JwksEndpoint, AddKendoJwt, and short-lived ServiceJwtMinter.
- **M0.2** Gateway AI integration: IFastAPIClient with Polly, RagController, UserSearchController, AssistantController, IntentAdvisoryMiddleware.
- **M0.3** Service Bus AI event contracts: EventIngested, EventValidated, UserEmbeddingUpdated, NotificationRequested events.
- **M0.4** AI queue topology: `kendo-events-ai` queue, dedicated producer/consumer registrations, DLQ monitoring.
- **M0.5** Worker AI handlers: Handlers for new events, IFastAPISummarizationClient, NotificationDispatcher.
- **M0.6** UserService Event domain + embeddings: Event/Validation/Embedding entities, vector column check constraints, internal admin write endpoint, fastapi_ro role.

> **Spec adoption note:** The architectural contracts for M0.1–M0.6 have already been pre-authored to ensure cross-component consistency. When the orchestrator picks up these milestones, it must ADOPT the existing specs in `docs/architecture/` (day_17_spec.md, day_18_spec.md, day_19_spec.md, day_20_spec.md) rather than generating them from scratch.

### Acceptance Criteria
- [ ] `docs/architecture/day_17_spec.md` (M0.1, M0.2) implemented and passing all tests.
- [ ] `docs/architecture/day_18_spec.md` (M0.3, M0.4) implemented and passing all tests.
- [ ] `docs/architecture/day_19_spec.md` (M0.5) implemented and passing all tests.
- [ ] `docs/architecture/day_20_spec.md` (M0.6) implemented and passing all tests.
- [ ] Gateway serves `/.well-known/jwks.json` and authenticates JWTs locally.
- [ ] `kendo-events-ai` queue is provisioned and isolated from `kendo-events`.
- [ ] Worker processes AI events idempotently without blocking user-lifecycle events.
- [ ] `fastapi_ro` role is enforced at the database level; FastAPI connections cannot write to pgvector.
- [ ] All Phase 01–03 acceptance criteria still pass.

### Architectural Components — Existing & Functioning at Phase-End

> Legend: ✅ complete · ❌ failed/blocked · ~ not yet started

| Component | Description | Status |
|---|---|---|
| `Gateway JWT Auth` | `RsaKeyProvider`, `JwksEndpoint`, local RS256 validation | ✅ |
| `Gateway FastAPI Client` | `IFastAPIClient` with Polly pipeline, RFC 7807 mapping | ✅ |
| `Gateway AI Controllers` | `RagController`, `UserSearchController`, `AssistantController` | ✅ |
| `Gateway Advisory Middleware` | `IntentAdvisoryMiddleware` with hard-coded fallback | ✅ |
| `AI Event Contracts` | 4 new `KendoMessage` types for AI workflows | ✅ |
| `AI Queue Topology` | `kendo-events-ai` queue, producers, consumers, DLQ | ✅ |
| `Worker AI Handlers` | 4 new Rebus message handlers, dispatcher service | ✅ |
| `Worker FastAPI Client` | `IFastAPISummarizationClient` for W7 streaming | ✅ |
| `UserService Event Domain` | `Event`, `EventValidation`, `UserEmbedding` entities | ✅ |
| `UserService Admin Endpoint` | `EmbeddingAdminController` protected by `admin:writes` scope | ✅ |
| `pgvector Roles` | `fastapi_ro` and `userservice_writer` roles provisioned | ✅ |
| *(All Phase 01 + 02 + 03 components)* | Inherited and still passing all prior acceptance criteria | ✅ (inherited) |

---

## Phase 05 — AI/Vector Service (FastAPI)

**Goal:** Add a dedicated, non-.NET AI/vector service for heavy and streaming LLM
workloads, **co-existing** with the .NET Semantic Kernel path. The .NET stack remains
primary for synchronous RAG inside `UserService`; FastAPI is the offload target the
Gateway calls for long-running, streaming, or batch LLM operations. This phase is
**planned, not yet started** — there is no Python code in `src/` today. The full spec
that drives implementation is `docs/architecture/fastapi_rag_service_spec.md`.

> **Resilience parity:** This phase introduces the first non-.NET workload. The README's
> "Polly" callout is .NET-specific; this phase uses the Python equivalents —
> `pybreaker` (circuit breaker), `tenacity` (retry with backoff + jitter), `asyncio`
> semaphores (bulkhead). All other platform standards (RFC 7807, OpenTelemetry,
> `/health/live` + `/health/ready`, `traceparent` propagation) apply unchanged.

### Milestone Markers

- **M5.1** Service scaffold: `src/FastAPIService/` Python 3.12 + FastAPI + Uvicorn
  containerized; `docker compose up fastapi` brings the service up; `/health/live` and
  `/health/ready` return correct codes.
- **M5.2** LangChain RAG pipeline: `POST /v1/rag/query` (sync) and `POST /v1/rag/stream`
  (SSE) implemented; end-to-end RAG against pgvector works using the same embedding
  model `UserService` uses for ingestion.
- **M5.3** Read-only pgvector integration: dedicated `fastapi_ro` PostgreSQL role
  provisioned by a `UserService` migration; FastAPI connects with read-only DSN; any
  write attempt is rejected by the database, not just by code.
- **M5.4** Gateway integration: `Gateway` exposes `/api/rag/{sync,stream}` and proxies
  to `FastAPIService` via a typed `IFastAPIClient` with Polly timeout + circuit breaker.
- **M5.5** Resilience parity: `pybreaker` circuit breakers per external dependency
  (pgvector, Azure OpenAI); `tenacity` retry with exponential backoff + jitter; no raw
  exceptions cross the network boundary — every error is RFC 7807.
- **M5.6** Observability + chaos + runbook: OpenTelemetry SDK wired with OTLP export;
  `traceparent` propagated from Gateway through FastAPI and embedded in SSE events;
  chaos test suite (DB down, Azure OpenAI down, FastAPI crash, slow stream) integrated
  into CI; `ops/runbooks/fastapi_service.md` published.

#### Workload sub-milestones (M5.7–M5.14)

> The M5.1–M5.6 sequence above is the **foundation** the workloads below layer on.
> Each workload is its own sub-milestone with its own gate, so a single workload's
> slip does not block the others. Workload definitions, data contracts, acceptance
> gates, and dependency maps live in `docs/architecture/fastapi_rag_service_spec.md`
> § Workload Catalog. The table below is the **roadmap-level** summary: priority,
> sequence, and which foundation milestones it consumes.

| Sub-milestone | Workload | Priority | Depends on foundation | Sequence |
|---|---|---|---|---|
| **M5.7** | W1 — Event Ingestion RAG | **P0** | M5.1, M5.2, M5.3, M5.4, M5.5, M5.6 | First P0 shipped after M5.6 |
| **M5.8** | W2 — Event Conflict & Schedule Reasoning | **P0** | M5.4 + W1 live | Immediately after M5.7 |
| **M5.14** | W8 — Evaluation & Regression Gate (DeepEval + Ragas) | **P0** | M5.1 + at least one workload's dataset | **Ships to CI before W1 ships to prod** |
| M5.9 | W3 — User Profile Semantic Search | P1 | M5.2, M5.3 + new `user_embeddings` migration | After P0 ships |
| M5.10 | W4 — User Intent Classification (advisory) | P1 | M5.1, M5.4, M5.5 | After P0 ships |
| M5.11 | W5 — Embeddings Backfill & Re-indexing (batch) | P1 | M5.2, M5.3 + `UserService` admin write endpoint | After W3 ships |
| M5.12 | W6 — Document Q&A / Onboarding Assistant (internal) | P2 | M5.1, M5.2, M5.3, M5.5, M5.6 | After P1 |
| M5.13 | W7 — Event Notification Summarization (Worker caller) | P2 | M5.5, M5.6 + new `IFastAPIClient` in `Kendo.Shared` | After P1 |

> **P0 rule:** W8 (M5.14) must be live in CI before W1 (M5.7) ships to production.
> The eval dataset for W8 starts empty (a 0% baseline) and grows with each
> workload that goes live. This is the platform's guarantee that no LLM change
> ships unmeasured.
>
> **P1/P2 rule:** P1 workloads may ship in any order after P0; P2 requires at
> least one P1 to be live (so the eval gate has real signal to compare against).

### Acceptance Criteria

- [ ] `docker compose up -d fastapi` starts the container; `/health/live` returns `200`
  with no dependency checks; `/health/ready` returns `200` only when pgvector and Azure
  OpenAI are reachable and the LangChain pipeline is loaded.
- [ ] FastAPI connects to pgvector as the `fastapi_ro` role; an `INSERT` from inside
  the FastAPI container is rejected by PostgreSQL with `permission denied` (role-level
  enforcement verified by an integration test).
- [ ] `POST /v1/rag/query` returns a coherent answer + scored contexts in the response
  body; latency p95 is within the agreed budget; `trace_id` field is populated.
- [ ] `POST /v1/rag/stream` emits SSE chunks in `text/event-stream`; each event carries a
  `traceparent` that links back to the originating Gateway span.
- [ ] Azure OpenAI circuit breaker trips after N consecutive failures; subsequent
  requests return RFC 7807 `503` (not a raw exception); the breaker recovers
  automatically after the cooldown.
- [ ] pgvector circuit breaker trips independently from the Azure OpenAI breaker (one
  dependency's failure does not trip the other).
- [ ] Retry policy retries transient DB failures with exponential backoff + jitter;
  retries are visible as span events in OpenTelemetry traces.
- [ ] OpenTelemetry trace IDs appear in every log line for a given FastAPI request
  (correlation enforced, same standard as Phases 01–03).
- [ ] Gateway's Polly circuit breaker trips when FastAPI is down; clients see RFC 7807
  `503` from the Gateway, not a connection refused.
- [ ] Rate limiter returns `429 Too Many Requests` with `Retry-After` above the
  configured per-JWT threshold.
- [ ] Chaos test suite runs in CI: DB down, Azure OpenAI down, FastAPI crash, slow
  stream — all produce a structured pass/fail report; pipeline fails on any regression.
- [ ] Runbook `ops/runbooks/fastapi_service.md` covers: Azure OpenAI outage, pgvector
  read replica failover, FastAPI crash loop, JWKS rotation.
- [ ] JWT validation succeeds with a Gateway-issued token; fails closed on expired,
  wrong-audience, or missing token.
- [ ] All Phase 01, 02, and 03 acceptance criteria still pass after FastAPI integration
  (regression guard).

#### Workload acceptance criteria (M5.7–M5.14)

> Each criterion gates the corresponding sub-milestone. Workload-level data contracts
> and resilience details live in `docs/architecture/fastapi_rag_service_spec.md` §
> Workload Catalog; the criteria below are the **roadmap gate** the orchestrator checks
> at Phase 4b review.

- [ ] **M5.7 (W1 — Event Ingestion RAG):** `POST /api/events/ingest` returns structured
  `Event` JSON for free-form text; p95 latency ≤ 8s; grounded answer rate ≥ 90% on a
  held-out DeepEval set; embedding model parity with `UserService` ingestion verified
  by a contract test.
- [ ] **M5.8 (W2 — Event Conflict & Schedule Reasoning):** `POST /api/events/{id}/validate`
  returns `{ ok, conflicts, suggestions }`; zero false-negatives on the conflict
  regression set; false-positive rate ≤ 5%; reasoning trace included with every
  response for auditability.
- [ ] **M5.14 (W8 — Evaluation & Regression Gate, P0):** `pytest tests/fastapi/eval/`
  runs in CI on every PR touching `src/FastAPIService/`; build fails on any metric
  regression > 5% vs. `main`; eval dataset is versioned in git; the suite completes
  in ≤ 10 minutes in CI.
- [ ] **M5.9 (W3 — User Profile Semantic Search):** `GET /api/users/search?q=...`
  returns hybrid pgvector + BM25 results; precision@10 ≥ 0.85 on the labeled query
  set; latency p95 ≤ 300ms.
- [ ] **M5.10 (W4 — User Intent Classification):** classification latency p95 ≤ 80ms;
  routing accuracy ≥ 95% on the intent test set; Gateway always has a hard-coded
  fallback route so a FastAPI outage degrades to "previous behavior" not a 500.
- [ ] **M5.11 (W5 — Embeddings Backfill & Re-indexing):** 10k events re-embedded in ≤ 5
  minutes on a 2-core container; idempotent (no duplicates on re-run); resumable
  (process death resumes from the last checkpoint, not from zero).
- [ ] **M5.12 (W6 — Document Q&A / Onboarding Assistant):** citation accuracy ≥ 95%
  on the corpus QA set; answer faithfulness ≥ 0.9; latency p95 ≤ 4s; index rebuild
  is debounced to ≤ 1 per 5 minutes on file changes.
- [ ] **M5.13 (W7 — Event Notification Summarization):** end-to-end notification
  latency p95 ≤ 6s; per-tenant tone control respected; prompt-version stored on the
  produced notification for audit; RFC 7807 on failure so the Worker can DLQ
  rather than retry-loop.
- [ ] All Phase 01–05 foundation criteria still pass after each workload ships
  (regression guard per workload).
- [ ] W8 (M5.14) is **live in CI before W1 (M5.7) ships to production** — the P0
  ordering rule is a hard gate, not a recommendation.

### Architectural Components — Existing & Functioning at Phase-End

> Legend: ✅ complete · ❌ failed/blocked · ~ not yet started

| Component | Description | Status |
|---|---|---|
| `FastAPIService` (Python) | `src/FastAPIService/` — FastAPI + Uvicorn, JWT auth, RFC 7807, OpenTelemetry, health endpoints | ✅ (Day 21) |
| `LangChain RAG Pipeline` | `RetrievalQA` chain, versioned prompt templates, sync + SSE streaming | ✅ (Day 22) |
| `pgvector Read-Only Role` | `fastapi_ro` PostgreSQL role provisioned by `UserService` migration; SELECT-only grants via asyncpg | ✅ (Day 22) |
| `pybreaker` | Circuit breaker per external dependency (pgvector, Azure OpenAI) with state metrics | ~ (planned) |
| `tenacity` Retry | Exponential backoff + jitter, transient-fault predicate, span-event visibility | ~ (planned) |
| `RFC 7807 Problem Details` | FastAPI exception handler returning `application/problem+json` with `trace_id` | ✅ (Day 21) |
| `OpenTelemetry (FastAPI)` | `opentelemetry-instrumentation-fastapi`, `-asyncpg`, `-httpx`; OTLP exporter | ~ (planned) |
| `Trace Correlation (FastAPI)` | `traceparent` extracted from Gateway request; embedded in every SSE event | ~ (planned) |
| `Gateway → FastAPI Client` | Typed `IFastAPIClient` with Polly timeout + circuit breaker; `/api/rag/{sync,stream}` route | ~ (planned) |
| `FastAPI Rate Limiter` | Token-bucket per JWT subject; `429 + Retry-After` | ~ (planned) |
| `FastAPI Chaos Suite` | xUnit (.NET side) + pytest (Python side); DB down, Azure OpenAI down, crash, slow stream | ~ (planned) |
| `FastAPI Runbook` | `ops/runbooks/day_21_runbook.md` — scaffold verification; `day_22_runbook.md` — RAG pipeline | ✅ (Day 21+22) |
| `FastAPI Dockerfile` | Multi-stage `python:3.12-slim`, non-root user, health check | ✅ (Day 21) |
| `docker compose` (FastAPI service) | Internal-network port `8000`, health check wired to Docker, ≥ 2 replicas when Phase 03 HA standards apply | ✅ (Day 21) |
| **W1 — Event Ingestion RAG** (M5.7) | `POST /api/events/ingest` — free-form text → structured `Event` JSON via LangChain + pgvector retrieval | ~ (planned) |
| **W2 — Event Conflict & Schedule Reasoning** (M5.8) | `POST /api/events/{id}/validate` — multi-step reasoning tool-use, audit trace returned | ~ (planned) |
| **W3 — User Profile Semantic Search** (M5.9) | `GET /api/users/search?q=...` — hybrid pgvector cosine + Postgres BM25 | ~ (planned) |
| **W4 — User Intent Classification** (M5.10) | Advisory router at Gateway, ≤ 80ms p95, hard-coded fallback in .NET | ~ (planned) |
| **W5 — Embeddings Backfill & Re-indexing** (M5.11) | `apscheduler` cron + `python -m app.jobs.reindex` CLI; writes via `UserService` admin endpoint (defense-in-depth) | ~ (planned) |
| **W6 — Document Q&A / Onboarding Assistant** (M5.12) | Internal tool; cited answers (file + line range); file-watcher debounced reindex | ~ (planned) |
| **W7 — Event Notification Summarization** (M5.13) | Worker calls FastAPI directly via new `IFastAPIClient` in `Kendo.Shared`; SSE streaming | ~ (planned) |
| **W8 — Evaluation & Regression Gate** (M5.14) | pytest + DeepEval + Ragas; CI artifact; fails build on > 5% regression vs. `main` | ~ (planned) |
| *(All Phase 01 + 02 + 03 components)* | Inherited and still passing all prior acceptance criteria; the .NET Semantic Kernel RAG path remains primary for synchronous RAG | ✅ (inherited) |
