# Kendo: Building an AI-Driven Software Factory on a Battle-Tested Microservice Platform

**TL;DR:** 6-phase gated orchestration pipeline + polyglot microservice platform with .NET 10, Python 3.12, pgvector, Azure Service Bus, and chaos-in-CI. 35 days, 8 phases, 380+ tests, all milestones shipped.

---

## 1. The Problem

Traditional software delivery follows a broken pattern. A product manager writes a spec. An architect designs a system. A developer implements it. A QA engineer tests it. An operations team deploys it. Each handoff loses context — the spec omits edge cases the developer discovers, the implementation diverges from the architect's intent, and the operations team inherits a system they didn't design with no runbook for what breaks.

For AI-augmented platforms specifically, the gap is worse. LLM workloads introduce non-deterministic behavior, latency variance, and cost uncertainty that traditional microservice patterns weren't designed for. A circuit breaker configured for a PostgreSQL query (typically <50ms) will false-positive on an OpenAI call that legitimately takes 15 seconds. A synchronous retry policy that works for a cache lookup will cascade into a resource exhaustion when applied to a streaming SSE endpoint.

Teams in this space report spending 40%+ of sprint time on context-switching, integration debt, and production incidents that shouldn't have been surprises.

---

## 2. The Business Use-Case

Kendo is designed for teams shipping AI-augmented products that need two things simultaneously:

1. **A reliable, observable microservice backend** — capable of handling user lifecycle, event management, authentication, and notification dispatch under production load, with circuit breakers, retry policies, graceful shutdown, and runbooks for every failure scenario.

2. **A disciplined delivery pipeline** — that treats every feature as a factory-processed unit from scope selection through architecture, implementation, validation, operations, review, and state update — all gated, all automated, all append-only traceable.

Concrete use-case: an event-driven platform where users create events (meetings, deadlines, tasks), the system semantically understands them via RAG, detects conflicts via LLM reasoning, classifies user intent to route requests, and sends summarized notifications — all while guaranteeing that an ingestion spike can't starve a notification summarization circuit breaker, and that every LLM response is measured against a regression gate before it reaches production.

---

## 3. Why This Approach?

The bet Kendo makes is that **discipline is a feature, not overhead.** The orchestration loop exists not because manual delivery is impossible, but because it produces invisible entropy — configuration drift, undocumented design decisions, unreviewed edge cases — that accumulates into incidents. By encoding the delivery process as a gated pipeline with artifact outputs at every phase, the platform ensures that nothing ships unmeasured, undocumented, or unreviewed.

The polyglot architecture (.NET 10 + Python 3.12) reflects a recognition that one language is the wrong tool for every job. .NET's type system, dependency injection, and mature async infrastructure make it ideal for transactional microservices. Python's ecosystem (LangChain, FastAPI, HuggingFace) is where the AI/LLM innovation happens. The gateway between them is a typed HTTP client with independent Polly pipelines — not shared state, not message-passing that obscures failure.

---

## 4. System Architecture (Mermaid Diagram Walkthrough)

The architecture diagram from `README.md` tells the full story. Here's a layer-by-layer breakdown:

### Load Balancer — NGINX (≥3 replicas)

NGINX sits at the edge, routing HTTPS traffic to the internal API Gateway. Health-check routing ensures traffic only reaches healthy replicas. The `X-Kendo-Replica` header injected at each service enables visibility into which replica handled each request — critical for debugging load-balancing issues during chaos tests.

### API Gateway (.NET 10)

The Gateway is the sole public entry point. It handles:
- **JWT authentication** — RS256 local validation against a JWKS endpoint, with automatic key rotation (90-day default, 7-day overlap window) via `KeyRotationBackgroundService`. Long-lived service JWTs and short-lived user JWTs coexist under the same issuer.
- **Rate limiting** — fixed-window per-permit limiter, returning `429 Too Many Requests` with `Retry-After` header.
- **Load shedding** — concurrency limiter that returns `503 Service Unavailable` (RFC 7807) under extreme concurrency, before the rate limiter in the middleware pipeline.
- **Polly resilience** — every outbound HTTP call is wrapped in a v8 pipeline: timeout (30s) → retry (3 attempts, exponential backoff + jitter) → circuit breaker (3 failures, 30s break).
- **RFC 7807 Problem Details** — `application/problem+json` on all 4xx/5xx responses, with `trace_id` populated from the active OpenTelemetry span.

### UserService (.NET 10)

The primary domain service. Uses EF Core with PostgreSQL 16 + pgvector for both relational data and vector embeddings. Semantic Kernel RAG pipeline (C#) runs synchronously. Noteworthy design choices:
- **Sidecar embedding tables** — `event_embeddings` and `user_embeddings` are separate tables (not columns on existing tables), keeping the original entity schemas immutable.
- **Environment-driven embedding dimensions** — enforced via the `kendo.embedding_dim` PostgreSQL GUC, set on application startup, preventing accidental dimension mismatches between services.
- **Defense-in-depth DB roles** — `userservice_writer` role with scoped connection for the EmbeddingAdminController; `fastapi_ro` read-only role for the FastAPI service.

### Worker (.NET 10)

An ASP.NET Core Web Application (for health endpoints) running `BackgroundService` consumers via Rebus with Azure Service Bus transport. Key capabilities:
- **Idempotent message handling** — `IdempotencyRecords` table with MessageId PK prevents duplicate processing. Crash recovery re-processes messages left in `Processing` state.
- **Dead Letter Queue monitoring** — `DlqDepthMonitor` polls ASB management API at configurable intervals, firing alerts when depth exceeds threshold.
- **Transactional outbox** — `OutboxMessage` table with filtered index (unprocessed only) ensures atomic event publishing; relay service publishes pending messages via Rebus `IBus.Send()`.
- **Independent Polly pipeline for FastAPI** — The Worker's `FastAPISummarizationClient` uses its own Polly pipeline (15s timeout, 3-failure → 30s circuit breaker), isolated from the Gateway's `IFastAPIClient`. This prevents W7 (summarization) from starving W1/W2 (ingestion/validation) of circuit breaker capacity.

### FastAPI Service (Python 3.12)

The first non-.NET workload, added deliberately as an offload target for heavy and streaming LLM operations. Key design:
- **LangChain RAG pipeline** — embedding → vector retrieval → context assembly → LLM answer, with versioned prompt templates. Both synchronous and SSE streaming endpoints.
- **pybreaker circuit breakers** — independent instances per external dependency (pgvector, Azure OpenAI) with state metrics. One failing dependency cannot trip the other.
- **tenacity retry** — exponential backoff + jitter, with a predicate that retries on 5xx but not 4xx (LLM errors are respected, not retried into a provider quota exhaustion).
- **Token-bucket rate limiter** — per JWT subject, with `429 Too Many Requests` response.
- **OpenTelemetry tracing** — `traceparent` extracted from incoming Gateway requests, propagated into every SSE event, enabling end-to-end trace correlation from user click through Gateway → FastAPI → OpenAI → SSE response.

### Azure Service Bus (Async Layer)

Chosen over RabbitMQ for cloud-native DLQ auto-provisioning, dead-letter management, and consistent routing. Two queues:
- `kendo-events` — general domain events (user lifecycle, outbox relay)
- `kendo-events-ai` — AI-specific events (ingestion, validation, embedding updates, notification requests), isolated to prevent AI workload congestion from blocking user-lifecycle processing

### Data Layer

- **PostgreSQL 16 + pgvector** — single source of truth for relational data and vector embeddings. Chosen over standalone vector DBs (Pinecone, Weaviate) to maintain ACID consistency between entities and their embeddings. The tradeoff point is ~1M vectors, at which point a dedicated vector DB can be introduced behind the same `fastapi_ro` role.
- **Redis 7** — distributed cache for replica identity and stateless session data. Best-effort: cache miss returns null, no cascading failure.

---

## 5. Architectural Decisions & Rationale

### Polyglot by Design

.NET 10 handles all transactional workloads (user lifecycle, event persistence, message bus integration) because its type system, DI container, and async infrastructure make correctness enforceable at compile time. Python 3.12 handles all AI/LLM workloads (RAG chains, intent classification, notification summarization) because Python's ML ecosystem is where the innovation happens. The boundary between them is a typed HTTP contract with separate resilience policies — not shared memory, not a queue that hides failure.

### pgvector over Standalone Vector DB

The argument for dedicated vector databases (Pinecone, Weaviate, Qdrant) is strong at hyperscale: dedicated indexing algorithms, higher-dimensional support, managed scalability. But for Kendo's use case, most embeddings live alongside relational data — an event's embedding is useless without the event's user, timestamp, and status. A separate vector DB introduces a distributed transaction problem for every write. pgvector inside PostgreSQL 16 gives ACID consistency for the entity↔embedding relationship, a single connection pool, and no additional operational surface.

### Per-Dependency Circuit Breakers

The FastAPI service uses independent pybreaker instances for pgvector and Azure OpenAI. This is a deliberate design choice: if the LLM provider is rate-limiting (a common scenario) or the vector DB is under maintenance, the other dependency continues to function. Without this separation, a pgvector outage would prevent all LLM operations even though the two systems are fully independent.

### Read-Only DB Role (`fastapi_ro`)

This is defense-in-depth enacted at the database level, not just in application code. Even if a bug, misconfiguration, or compromised dependency causes the FastAPI service to issue a write query, PostgreSQL rejects it. The `userservice_writer` role used by the EmbeddingAdminController requires a scoped connection string, making privilege escalation impossible through normal request flow.

### Independent Polly Pipelines

The Gateway's `IFastAPIClient` and the Worker's `INotificationSummarizationClient` have separate Polly configurations. This was a lesson learned after designing Phase 01's shared resilience pipeline: a single circuit breaker means one misbehaving dependency can block all traffic through that client. With independent pipelines, a spike in notification summarization traffic may trip the Worker's breaker without affecting Gateway-routed RAG queries.

### Three-Pillar Async Reliability

The async messaging layer implements three complementary patterns:
1. **Transactional Outbox** — events are persisted atomically with the database transaction that creates them. The outbox relay publishes them to ASB, with retry up to 5 attempts before the record is flagged for manual review.
2. **Idempotency Keys** — every message carries a `MessageId` that serves as the idempotency key. The Worker checks `IdempotencyRecords` before processing; duplicate messages are silently acknowledged. Crash recovery re-processes messages left in `Processing` state.
3. **Traceparent Propagation** — the W3C `traceparent` header is captured from the ambient OpenTelemetry `Activity.Current?.Id` at outbox write time, forwarded as a Rebus header, and extracted in the Worker handler to create a child span linked to the producer trace. End-to-end trace correlation without coupling the producer and consumer.

### Chaos-in-CI

The chaos test suite runs on every PR touching infrastructure-related code. Three xUnit chaos scenarios (DB downtime, service crash, network partition) simulate catastrophic failure and verify that circuit breakers trip, fallback responses return, and metrics reflect the degradation. The bash-based supplement (`scripts/chaos/run_all.sh`) adds Docker-level chaos (container pause, kill, network delay). CI fails if any chaos regression is detected.

---

## 6. The Orchestration Loop — Deep Dive

The orchestration pipeline processes daily milestones into production-ready code through a strict, 6-phase gated loop. Each phase produces named artifacts. Gates between phases are hard stops — no phase may be skipped, and the REVIEWER FAIL verdict discards all current-day artifacts and restarts from Phase 1.

### Phase 0 — State Initialization & Scope Derivation

The Orchestrator loads `.ai/current_state.md` and `docs/platform_roadmap.md` to determine what to ship next. Before any new work begins:

1. **Prior-day sealing check** — if `current_day > 1`, Phase 0 verifies that Phase 5 sealed the previous session: `validate_state.sh` for the prior day exits 0, the `## Completed Days` table has a row, and `incomplete_tasks` is empty. A failing prior-day seal is a hard blocker.
2. **Documentation gap scan** — all `## Completed Days` rows are checked for missing review reports or runbooks. Gaps are surfaced as `mild` carry-forward items (non-blocking).
3. **Dependency-aware milestone selection** — the Orchestrator reads the roadmap's `### Components` table, identifies all `~` (not-started) rows, and picks the first one whose dependencies (checked against `## Active Dependency Map`) are all satisfied. This means W5 (embeddings backfill) can ship before W3 (semantic search) if W3 requires `user_embeddings` that W5 creates.
4. **Variable resolution** — `{{DAY_NUMBER}}`, `{{MILESTONE}}`, `{{BRANCH_BASE}}`, and `{{phase_plan}}` are resolved from the current state and roadmap.

### Phase 1 — Architecture & Contract Design

The Platform Architect agent produces `docs/architecture/day_{{DAY_NUMBER}}_spec.md` — a detailed specification covering milestone scope, layer changes, data contracts, implementation plan (ordered commit units), success checklist (mapping 1:1 to roadmap acceptance criteria), resilience mandate, and dependency citations.

For pre-authored designs (like Phase 04's JWT auth contracts), the spec is "adopted" rather than regenerated from scratch, preserving cross-component consistency.

### Phase 2 — Implementation + Validation (Paired Developer + QA)

This is where the discipline becomes visible. Each commit unit from Phase 1's implementation plan is executed in sequence — not batched. For each unit:
1. Developer implements all files
2. QA writes or updates tests for that unit
3. The gate command runs (lint + test) — **must exit 0**. Any failure halts the unit
4. On green: `git add -A && git commit`

The constraint that tests must exist at commit time (not after implementation) eliminates the "I'll write tests later" pattern that produces untested code.

### Phase 4 — Delivery & Operations (DevOps)

The DevOps/SRE agent updates Dockerfiles, CI pipeline, and produces `ops/runbooks/day_{{DAY_NUMBER}}_runbook.md` — a runbook documenting:
- What was deployed
- Rollback plan
- Health check validation
- Incident response steps

### Phase 4b — Review & Merge

The Reviewer agent reads all artifacts (spec, commit log, test output, runbook) and issues a verdict:

- **PASS** → open PR against `develop`, wait for CI, squash-merge, delete branch → proceed to Phase 5
- **FAIL** → enumerate blockers mapped to specific agents, discard ALL current-day artifacts (spec, commit log, branch, runbook), update `current_state.md` with blockers in `incomplete_tasks`, **restart from Phase 1**

This is not a soft retry. FAIL means the entire day's work is discarded. The factory rejects the defective batch.

### Phase 5 — State Update & Changelog

The Orchestrator performs 10 mandatory operations: update `## Completed Days`, append to `## Active Dependency Map`, replace `## Active Infrastructure Snapshot` with current state, resolve carry-forward items, log architectural decisions, write the `## Last Session Summary`, update the roadmap component table, write the daily changelog, run `validate_state.sh`, and increment `current_day`.

All writes are **append-only** — history is never truncated or rewritten, except for the infrastructure snapshot and last session summary which are full replacements.

---

## 7. Evolution of the Orchestration Loop

The orchestration loop wasn't designed upfront in its current form. It evolved through three key changes during the 35-day delivery cycle:

### 1. Dependency-Aware Milestone Ordering (Day 32)

**Before:** Scope derivation picked the first `~` row in the roadmap component table. This assumed linear dependency chains — that milestone N+1 always depends on milestone N.

**Problem:** Phase 05 introduced complex cross-milestone dependencies. W3 (User Profile Semantic Search) depended on `user_embeddings` being populated, which W5 (Embeddings Backfill) created. A strictly linear scan would schedule W3 before W5 — the exact wrong order.

**Solution:** The Phase 0 scope derivation now scans ALL `~` candidates and picks the first one whose dependencies (checked against `## Active Dependency Map` and all completed rows) are satisfied. If the chosen milestone isn't the first `~` row, the selection rationale is logged to `## Architectural Decisions Log`.

### 2. Prior-Day Sealing Gate (Day 32)

**Before:** Sessions were sequenced by convention — the developer was expected to ensure Phase 5 completed before starting the next day.

**Problem:** No enforcement meant that partial state (incomplete Phase 5, unvalidated artifacts) could cascade: Day N's state would be modified by Day N+1's Phase 0, making rollback or audit impossible.

**Solution:** Phase 0 now runs a prior-day sealing check before any scope derivation. It verifies:
- `./scripts/validate_state.sh $((current_day - 1))` exits 0
- `## Completed Days` has a row for the prior day
- `current_phase: 0` AND `incomplete_tasks: []`

A failing seal is a hard blocker — `current_day` is not advanced.

### 3. Documentation Gap Scan (Day 32)

**Before:** Documentation (review reports, runbooks) was assumed to be present if Phase 4/4b completed successfully.

**Problem:** The initial phases produced review reports and runbooks as a matter of course, but as the project accelerated through Phase 05 workloads (shipping multiple milestones per week), documentation began falling through the cracks — a milestone might ship without an updated runbook.

**Solution:** Phase 0 now scans all completed days for missing review reports and runbooks. Gaps are surfaced as `mild` carry-forward items. **Non-blocking** — documentation gaps don't prevent day advancement — but they're logged as technical debt with severity tags, making them visible for reconciliation PRs.

### 4. Phase 4b FAIL = Hard Restart (Initial Design)

**Initial approach:** A failing review would update `current_state.md` with blockers and restart from Phase 2 (re-implementation), keeping the already-produced architecture spec.

**Problem:** If the spec itself had flaws — incorrect data contracts, wrong layer delineation, missing resilience mandate — restarting from Phase 2 would re-implement against a broken foundation. The architecture layer's errors would silently propagate.

**Solution:** Phase 4b FAIL now discards ALL current-day artifacts — spec, commit log, branch, runbook — and **restarts from Phase 1**. The Reviewer enumerates blockers with specific action items per responsible agent, and the Phase 1 agent must produce a corrected spec. This is more expensive (a full day's work may be discarded) but ensures that architectural defects are caught at the architecture layer, not compensated for at the implementation layer.

### 5. Spec Adoption Model (Phase 04)

**Initial approach:** Every milestone produced a brand-new architecture spec via the Phase 1 agent.

**Problem:** Phase 04 (Pre-FastAPI Reconciliation) introduced 6 milestones (M0.1–M0.6) that were tightly coupled — M0.1's JWT auth foundation had to be consistent with M0.5's Worker FastAPI client, which had to be consistent with M0.6's Event domain entities. Generating specs independently risked subtle contract mismatches.

**Solution:** Phase 04's specs were hand-authored before any implementation began. The orchestrator **adopts** these pre-authored specs rather than generating them from scratch. The `## Depends on` section in each spec explicitly cites which other specs it adopts from, enabling the reviewer to verify cross-spec consistency before any code is written.

---

## 8. 35 Days of Incremental Delivery

The platform was built across 8 phases, each broken into milestones delivered across 35 days:

| Phase | Name | Days | Key Deliverables |
|-------|------|------|-----------------|
| 01 | Core APIs & Synchronous Resilience | 6 | Gateway, UserService, Worker, Polly, OpenTelemetry, RFC 7807, pgvector |
| 02 | Asynchronous Decoupling | 6 | Rebus + ASB, 202 endpoints, idempotency, DLQ, outbox, traceparent |
| 03 | High Availability & Chaos | 6 | NGINX LB, Redis, chaos suite, rate limiting, graceful shutdown, runbooks |
| 04 | Pre-FastAPI Reconciliation | 6 | JWT auth, AI event contracts, queue topology, Worker AI handlers, Event domain |
| 05 | AI/Vector Service (FastAPI) | 11 | 14 milestones (W1–W8), LangChain RAG, intent classification, notification summarization, eval gate |

All 8 workloads (W1–W8) are live on `develop`. The W8 Evaluation Gate (DeepEval + Ragas) runs in CI on every PR — the platform's guarantee that no LLM change ships unmeasured.

---

## 9. Test Coverage & CI

| Suite | Framework | Count | Run Command |
|-------|-----------|-------|-------------|
| .NET Unit | xUnit | ~130 | `dotnet test --filter "Category=Unit"` |
| .NET Data | xUnit | ~25 | `dotnet test --filter "Category=Data"` |
| .NET Resilience | xUnit | ~20 | `dotnet test --filter "Category=Resilience"` |
| .NET Messaging | xUnit | ~15 | `dotnet test --filter "Category=Messaging"` |
| FastAPI Unit | pytest | 136 | `pytest tests/fastapi/` |
| FastAPI Chaos | pytest | 4 | `pytest tests/chaos/ --chaos` |
| W8 Eval Gate | DeepEval + Ragas | ~20 | `pytest tests/fastapi/eval/` |
| Chaos Scenarios | bash | 3 | `scripts/chaos/run_all.sh` |

**CI Pipeline:** `build-and-test` → `docker-compose` (multi-replica health verification) → `chaos-test` → `eval-gate` (FastAPI PRs only)

---

## 10. What's Next

| Phase | Name | Status | Scope |
|-------|------|--------|-------|
| 06 | Production Readiness | In progress | README cleanup, CI hardening, documentation gap closure |
| 07 | Angular GUI | Planned | Web dashboard for platform management |
| 08 | Hetzner Deployment | Planned | Terraform + Docker Compose on Hetzner Cloud |

Phase 06 focuses on the unglamorous but essential work of production hardening — verifying that every operational runbook is accurate, every health check has the right timeout, and every CI job produces actionable failure output. Phase 07 introduces the first frontend (Angular), exposing the platform's capabilities through a GUI. Phase 08 targets a production Hetzner deployment with Terraform infrastructure-as-code.

---

*Kendo is open source. The orchestration loop, target platform source code, and all 35+ days of architecture specs are available in the repository. Contributions and questions welcome.*

🌸 Generated with [AdaL](https://github.com/adal-cli/)
