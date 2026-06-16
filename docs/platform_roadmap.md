# Target Platform Resilience Roadmap
> **Orchestration reference.** Loaded by the Entrypoint for the `{{phase_plan}}` section in scope.  
> Each phase defines the milestone markers, acceptance criteria, and architectural components  
> that must exist and function **at phase-end** before the next phase begins.  
> Last updated: 2026-06-16

> **Phase numbering note:** Phases 01–03 are **complete**. The orchestrator (`.ai/current_state.md`)
> currently has **Phase 04 — Observability & Validation Hardening** reserved as the next
> planned phase. **Phase 05 — AI/Vector Service (FastAPI)** is introduced here to capture
> the aspirational FastAPI workload referenced in `README.md` and detailed in
> `docs/architecture/fastapi_rag_service_spec.md`. Phase 04 will be authored when the
> orchestrator picks up that work; this numbering is intentional and does not collide.

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

### Architectural Components — Existing & Functioning at Phase-End

> Legend: ✅ complete · ❌ failed/blocked · ~ not yet started

| Component | Description | Status |
|---|---|---|
| `FastAPIService` (Python) | `src/FastAPIService/` — FastAPI + Uvicorn, JWT auth, RFC 7807, OpenTelemetry, health endpoints | ~ (planned) |
| `LangChain RAG Pipeline` | `RetrievalQA` chain, versioned prompt templates, sync + SSE streaming | ~ (planned) |
| `pgvector Read-Only Role` | `fastapi_ro` PostgreSQL role provisioned by `UserService` migration; SELECT-only grants | ~ (planned) |
| `pybreaker` | Circuit breaker per external dependency (pgvector, Azure OpenAI) with state metrics | ~ (planned) |
| `tenacity` Retry | Exponential backoff + jitter, transient-fault predicate, span-event visibility | ~ (planned) |
| `RFC 7807 Problem Details` | FastAPI exception handler returning `application/problem+json` with `trace_id` | ~ (planned) |
| `OpenTelemetry (FastAPI)` | `opentelemetry-instrumentation-fastapi`, `-asyncpg`, `-httpx`; OTLP exporter | ~ (planned) |
| `Trace Correlation (FastAPI)` | `traceparent` extracted from Gateway request; embedded in every SSE event | ~ (planned) |
| `Gateway → FastAPI Client` | Typed `IFastAPIClient` with Polly timeout + circuit breaker; `/api/rag/{sync,stream}` route | ~ (planned) |
| `FastAPI Rate Limiter` | Token-bucket per JWT subject; `429 + Retry-After` | ~ (planned) |
| `FastAPI Chaos Suite` | xUnit (.NET side) + pytest (Python side); DB down, Azure OpenAI down, crash, slow stream | ~ (planned) |
| `FastAPI Runbook` | `ops/runbooks/fastapi_service.md` — failure-mode playbook | ~ (planned) |
| `FastAPI Dockerfile` | Multi-stage `python:3.12-slim`, non-root user, health check | ~ (planned) |
| `docker compose` (FastAPI service) | Internal-network port `8000`, health check wired to Docker, ≥ 2 replicas when Phase 03 HA standards apply | ~ (planned) |
| *(All Phase 01 + 02 + 03 components)* | Inherited and still passing all prior acceptance criteria; the .NET Semantic Kernel RAG path remains primary for synchronous RAG | ✅ (inherited) |
