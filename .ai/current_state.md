# Infraspekt — Current State
> **Live checkpoint.** Updated by the Orchestrator at the end of every phase.  
> Rule: never truncate history. Append only — except `## Last Session Summary` and `## Active Infrastructure Snapshot` (full replacements).  
> Last updated: 2026-06-19 (Day 21)

---

## Session Variables

```yaml
current_day: 22
current_phase: 1        # Phase 1 complete — day_22_spec.md created
branch_base: develop
feature_branch: TBD  # resolved by Phase 1 {{SLUG}}
phase_plan: "05"        # AI/Vector Service (FastAPI)
```

---

## Last Session Summary
> Replaced each session. 3-bullet hand-off note for the next run.

* **M5.1 complete** — FastAPI Service Scaffold (Python 3.12 + FastAPI + Uvicorn) implemented across 15 files, ~393 additions. Service containerized with multi-stage Dockerfile, health endpoints (/health/live, /health/ready), JWT auth middleware (RS256, health exempt), and RFC 7807 exception handler. All 4 commit units committed, zero halted. docker-compose integration: internal port 8000, depends_on postgres, HEALTHCHECK. CI updated to 7-service health check. PR #27 squash-merged into `develop`.
* **Phase 05 foundation started** — M5.1 scaffold complete. 3 components now ✅ (FastAPIService Python, FastAPI Dockerfile, docker-compose integration, RFC 7807 handler, day runbook). Next: M5.2 — LangChain RAG pipeline.
* Carry-forward maintained: chaos-test CI flakiness (`test_db_downtime`) — still documented in M3.6 runbooks. Day 17 open questions (IssuerSigningKeyResolver refactor, no user JWT issuance, no rotation BackgroundService) carried forward.

---

## Phase Outputs — Day 01
> Legend: ✅ complete · ❌ failed/blocked · ~ pending · ⏳ deferred

| Phase | Artifact | Status |
|---|---|---|
| 0 | State initialized, variables resolved | ✅ |
| 1 | `docs/architecture/day_01_spec.md` | ✅ |
| 2 | Commit log — zero halted units — feature branch on `origin` | ✅ |
| 4 | `ops/Dockerfile` · `.github/workflows/ci.yml` · `ops/runbooks/day_01_runbook.md` | ✅ |
| 4b | `docs/architecture/day_01_review_report.md` + PR merged to `develop` | ✅ |
| 5 | `changelog/2026-06-13.md` entry · `validate_state.sh` exit 0 | ✅ |

---

## Phase Outputs — Day 02
> Legend: ✅ complete · ❌ failed/blocked · ~ pending · ⏳ deferred

| Phase | Artifact | Status |
|---|---|---|
| 0 | State initialized, variables resolved → M1.2 Database layer | ✅ |
| 1 | `docs/architecture/day_02_spec.md` | ✅ |
| 2 | Commit log — zero halted units — feature branch on `origin` | ✅ |
| 4 | `ops/Dockerfile` · `.github/workflows/ci.yml` · `ops/runbooks/day_02_runbook.md` | ✅ |
| 4b | `docs/architecture/day_02_review_report.md` + PR merged to `develop` | ✅ |
| 5 | `changelog/2026-06-13.md` entry · `validate_state.sh` exit 0 | ✅ |

---

## Phase Outputs — Day 03
> Legend: ✅ complete · ❌ failed/blocked · ~ pending · ⏳ deferred

| Phase | Artifact | Status |
|---|---|---|
| 0 | State initialized, variables resolved → M1.3 Resilience baseline | ✅ |
| 1 | `docs/architecture/day_03_spec.md` | ✅ |
| 2 | Commit log — zero halted units — feature branch on `origin` | ✅ |
| 4 | `ops/Dockerfile` · `.github/workflows/ci.yml` · `ops/runbooks/day_03_runbook.md` | ✅ |
| 4b | `docs/architecture/day_03_review_report.md` + PR merged to `develop` | ✅ |
| 5 | `changelog/2026-06-13.md` entry · `validate_state.sh` exit 0 | ✅ |

---

## Phase Outputs — Day 04
> Legend: ✅ complete · ❌ failed/blocked · ~ pending · ⏳ deferred

| Phase | Artifact | Status |
|---|---|---|
| 0 | State initialized, variables resolved → M1.4 Observability foundation | ✅ |
| 1 | `docs/architecture/day_04_spec.md` | ✅ |
| 2 | Commit log — zero halted units — feature branch on `origin` | ✅ |
| 4 | `ops/Dockerfile` · `.github/workflows/ci.yml` · `ops/runbooks/day_04_runbook.md` | ✅ |
| 4b | `docs/architecture/day_04_review_report.md` + PR merged to `develop` | ✅ |
| 5 | State update, roadmap update, changelog, validation | ✅ |

---

## Phase Outputs — Day 05
> Legend: ✅ complete · ❌ failed/blocked · ~ pending · ⏳ deferred

| Phase | Artifact | Status |
|---|---|---|
| 0 | State initialized, variables resolved → M1.5 RFC 7807 Problem Details | ✅ |
| 1 | `docs/architecture/day_05_spec.md` | ✅ |
| 2 | Commit log — zero halted units — feature branch on `origin` | ✅ |
| 4 | `ops/Dockerfile` · `.github/workflows/ci.yml` · `ops/runbooks/day_05_runbook.md` | ✅ |
| 4b | `docs/architecture/day_05_review_report.md` + PR merged to `develop` | ✅ |
| 5 | State update, roadmap update, changelog, validation | ✅ |

---

## Phase Outputs — Day 06
> Legend: ✅ complete · ❌ failed/blocked · ~ pending · ⏳ deferred

| Phase | Artifact | Status |
|---|---|---|
| 0 | State initialized, variables resolved → M2.1 Rebus + ASB wired | ✅ |
| 0b | *Skipped* (repo has prior commits) | ✅ |
| 1 | `docs/architecture/day_06_spec.md` | ✅ |
| 2 | Commit log — zero halted units — feature branch on `origin` | ✅ |
| 4 | `ops/Dockerfile` milestone label · `.github/workflows/ci.yml` (messaging step + Wait-for-healthy fix) · `ops/runbooks/day_06_runbook.md` | ✅ |
| 4b | `docs/architecture/day_06_review_report.md` + PR #7 merged to `develop` | ✅ |
| 5 | State update, roadmap update, changelog, validation | ✅ |

---

## Phase Outputs — Day 11
> Legend: ✅ complete · ❌ failed/blocked · ~ pending · ⏳ deferred

| Phase | Artifact | Status |
|---|---|---|
| 0 | State initialized, variables resolved → M2.6 Traceparent propagation | ✅ |
| 0b | *Skipped* (repo has prior commits) | ✅ |
| 1 | `docs/architecture/day_11_spec.md` | ✅ |
| 2 | Commit log — 4/4 units committed, zero halted — feature branch on `origin` | ✅ |
| 4 | `ops/Dockerfile` milestone label · `ops/runbooks/day_11_runbook.md` | ✅ |
| 4b | `docs/architecture/day_11_review_report.md` + PR #12 merged to `develop` | ✅ |
| 5 | State update, roadmap update, changelog, validation | ✅ |

---

## Phase Outputs — Day 09
> Legend: ✅ complete · ❌ failed/blocked · ~ pending · ⏳ deferred

| Phase | Artifact | Status |
|---|---|---|
| 0 | State initialized, variables resolved → M2.4 DLQ consumer + alerting | ✅ |
| 0b | *Skipped* (repo has prior commits) | ✅ |
| 1 | `docs/architecture/day_09_spec.md` | ✅ |
| 2 | Commit log — 5/5 units committed, zero halted — feature branch on `origin` | ✅ |
| 4 | `ops/Dockerfile` milestone label · `ops/runbooks/day_09_runbook.md` | ✅ |
| 4b | Review + PR #10 merged to `develop` | ✅ |
| 5 | State update, roadmap update, changelog, validation | ✅ |

---

## Phase Outputs — Day 13
> Legend: ✅ complete · ❌ failed/blocked · ~ pending · ⏳ deferred

| Phase | Artifact | Status |
|---|---|---|
| 0 | State initialized, variables resolved → M3.3 Chaos suite | ✅ |
| 0b | *Skipped* (repo has prior commits) | ✅ |
| 1 | `docs/architecture/day_13_spec.md` | ✅ |
| 2 | Commit log — 5/5 units committed, zero halted — feature branch on `origin` | ✅ |
| 4 | `ops/runbooks/day_13_runbook.md` — `chaos-test` job in `.github/workflows/ci.yml` | ✅ |
| 4b | `docs/architecture/day_13_review_report.md` + PR #16 merged to `develop` | ✅ |

---

## Phase Outputs — Day 14
> Legend: ✅ complete · ❌ failed/blocked · ~ pending · ⏳ deferred

| Phase | Artifact | Status |
|---|---|---|
| 0 | State initialized, variables resolved → M3.4 Rate limiting & shedding | ✅ |
| 0b | *Skipped* (repo has prior commits) | ✅ |
| 1 | `docs/architecture/day_14_spec.md` | ✅ |
| 2 | Commit log — 4/4 units committed, zero halted — feature branch on `origin` | ✅ |
| 4 | `ops/runbooks/day_14_runbook.md` | ✅ |
| 4b | `docs/architecture/day_14_review_report.md` + PR #17 merged to `develop` | ✅ |
| 5 | State update, roadmap update, changelog, validation | ✅ |

---

## Phase Outputs — Day 15
> Legend: ✅ complete · ❌ failed/blocked · ~ pending · ⏳ deferred

| Phase | Artifact | Status |
|---|---|---|
| 0 | State initialized, variables resolved → M3.5 Graceful shutdown | ✅ |
| 0b | *Skipped* (repo has prior commits) | ✅ |
| 1 | `docs/architecture/day_15_spec.md` | ✅ |
| 2 | Commit log — 5 units committed, zero halted — feature branch on `origin` | ✅ |
| 4 | `ops/runbooks/day_15_runbook.md` | ✅ |
| 4b | `docs/architecture/day_15_review_report.md` + PR #19 merged to `develop` | ✅ |
| 5 | State update, roadmap update, changelog, validation | ✅ |

---

## Phase Outputs — Day 16
> Legend: ✅ complete · ❌ failed/blocked · ~ pending · ⏳ deferred

| Phase | Artifact | Status |
|---|---|---|
| 0 | State initialized, variables resolved → M3.6 Ops Runbooks | ✅ |
| 0b | *Skipped* (repo has prior commits) | ✅ |
| 1 | `docs/architecture/day_16_spec.md` | ✅ |
| 2 | Commit log — 5/5 units committed, zero halted — feature branch on `origin` | ✅ |
| 4 | `ops/runbooks/` — 4 scenario playbooks + central index | ✅ |
| 4b | `docs/architecture/day_16_review_report.md` + PR #20 merged to `develop` | ✅ |
| 5 | State update, roadmap update, changelog, validation | ✅ |

---

## Phase Outputs — Day 18
> Legend: ✅ complete · ❌ failed/blocked · ~ pending · ⏳ deferred

| Phase | Artifact | Status |
|---|---|---|
| 0 | State initialized, variables resolved → M0.3+M0.4 AI Event Contracts + Queue Topology | ✅ |
| 0b | *Skipped* (repo has prior commits) | ✅ |
| 1 | `docs/architecture/day_18_spec.md` (adopted, pre-authored) | ✅ |
| 2 | Commit log — 7/7 units committed, zero halted — feature branch on `origin` | ✅ |
| 4 | `ops/runbooks/day_18_runbook.md` · `docker-compose.yml` (AI queue env vars) · `.env.example` | ✅ |
| 4b | `docs/architecture/day_18_review_report.md` + PR #23 merged to `develop` | ✅ |
| 5 | State update, roadmap update, changelog, validation | ✅ |

---

## Phase Outputs — Day 20
> Legend: ✅ complete · ❌ failed/blocked · ~ pending · ⏳ deferred

| Phase | Artifact | Status |
|---|---|---|
| 0 | State initialized, variables resolved → M0.6 UserService Event Domain | ✅ |
| 0b | *Skipped* (repo has prior commits) | ✅ |
| 1 | `docs/architecture/day_20_spec.md` (adopted, pre-authored) | ✅ |
| 2 | Commit log — 9/9 units committed, zero halted — feature branch on `origin` | ✅ |
| 4 | `ops/runbooks/day_20_runbook.md` · `docker-compose.yml` (embedding env vars) · `.env.example` | ✅ |
| 4b | `docs/architecture/day_20_review_report.md` + PR #26 merged to `develop` | ✅ |
| 5 | State update, roadmap update, changelog, validation | ✅ |

---

## Phase Outputs — Day 19
> Legend: ✅ complete · ❌ failed/blocked · ~ pending · ⏳ deferred

| Phase | Artifact | Status |
|---|---|---|
| 0 | State initialized, variables resolved → M0.5 Worker AI Handlers + W7 | ✅ |
| 0b | *Skipped* (repo has prior commits) | ✅ |
| 1 | `docs/architecture/day_19_spec.md` (adopted, pre-authored) | ✅ |
| 2 | Commit log — 7/7 units committed, zero halted — feature branch on `origin` | ✅ |
| 4 | `ops/runbooks/day_19_runbook.md` · `docker-compose.yml` (FastAPI summarization env vars) · `.env.example` | ✅ |
| 4b | `docs/architecture/day_19_review_report.md` + PR #24 merged to `develop` | ✅ |
| 5 | State update, roadmap update, changelog, validation | ✅ |

---

## Phase Outputs — Day 21
> Legend: ✅ complete · ❌ failed/blocked · ~ pending · ⏳ deferred

| Phase | Artifact | Status |
|---|---|---|
| 0 | State initialized, variables resolved → M5.1 FastAPI Service Scaffold | ✅ |
| 0b | *Skipped* (repo has prior commits) | ✅ |
| 1 | `docs/architecture/day_21_spec.md` (adopted pre-authored `fastapi_rag_service_spec.md`) | ✅ |
| 2 | Commit log — 4/4 units committed, zero halted — feature branch on `origin` | ✅ |
| 4 | `ops/runbooks/day_21_runbook.md` · `docker-compose.yml` (fastapi service) · `.env.example` · CI health count bump | ✅ |
| 4b | PR #27 squash-merged to `develop` | ✅ |
| 5 | State update, roadmap update, changelog, validation | ✅ |

---

## Incomplete Tasks
> Tasks started this day but halted (lint/test failure, spec ambiguity, reviewer FAIL routing).  
> **Must be empty before Day N+1 can begin.** Populated by Reviewer FAIL verdict or commit-gate halt.

- (none)

---

## Deferred Tasks
> Tasks intentionally descoped today; flagged for a future day or phase.  
> Does NOT block day advancement — logged here for traceability.  
> Each item must reference the day it was deferred FROM and the target day/milestone.

- (none)

---

## Carry-Forward Items
> Open blockers, homework, and unresolved decisions. Remove when resolved; append when new ones arise.

- **chaos-test CI flakiness (Day 13/15/16):** `test_db_downtime` chaos test intermittently fails in CI returning `000000` (connection refused) instead of expected 503. **Fully documented in M3.6 runbooks** — see `ops/runbooks/db-failover.md` §Known CI Flakiness for symptom, suspected root cause, and 3 mitigation approaches. Not blocking CI (~90% pass rate). Carry-forward maintained for engineering action — recommended mitigation #2 (retry loop after `docker compose unpause`) should be applied to `scripts/chaos/test_db_downtime.sh`.
- **Day 17 — open questions for closure refactoring:** `IssuerSigningKeyResolver` uses `BuildServiceProvider()` anti-pattern — needs closure-based refactor. User JWT issuance not implemented (validation only). Key rotation has no `BackgroundService` — manual only. Carried forward for Day 19+/clean-up sprint.

---

## Completed Days

| Day | Title | Key Outputs | Notes | Status |
|---|---|---|---|---|
| 00 | Repo Scaffolding | `.ai/` structure, GitHub Actions baseline | Phase 0b bootstrap committed | ✅ |
| 01 | Multi-service scaffold | Gateway + UserService + Worker, Docker Compose, CI pipeline, 8 tests | PR #2 merged to develop | ✅ |
| 02 | Database layer | PostgreSQL 16 + pgvector, EF Core DbContext, initial migration, connection string externalized, 12 tests | PR #3 merged to develop | ✅ |
| 03 | Resilience baseline | Polly Retry + Circuit Breaker on DB + HTTP clients, shared resilience pipeline, 25 tests (13 resilience), CI updated | PR #4 merged to develop | ✅ |
| 04 | Observability foundation | OpenTelemetry console exporter, trace-ID correlation, structured Polly logging, CI PostgreSQL fix, 29 tests (4 observability) | PR #5 merged to develop | ✅ |
| 05 | RFC 7807 Problem Details | `KendoProblemDetails` DTO + `ProblemDetailsMiddleware` in Kendo.Shared, wired into all 3 services, 10 new tests, Dockerfile context fix | PR #6 merged to develop | ✅ |
| 06 | Rebus + Azure Service Bus wired | `KendoMessage` + `KendoRebusConfiguration` in Kendo.Shared; Gateway + UserService producers, Worker consumer; 4 Messaging tests; CI fix (Wait for all 4 healthy) | PR #7 merged to develop | ✅ |
| 07 | First async endpoint (M2.2) | `UsersController` (POST 202 + GET status), `User` entity, `UserCreatedEvent`, EF migration, 9 new tests, runbook | PR #8 merged to develop | ✅ |
| 08 | Background consumer (M2.3) | `UserCreatedEventHandler`, `IdempotencyRecords` table, idempotency enforcement, crash recovery, 7 new handler tests, runbook | PR #9 merged to develop | ✅ |
| 09 | DLQ consumer & alerting (M2.4) | `DeadLetteredMessage` + `KendoRebusDlqConfiguration` in Shared; `DeadLetterHandler`, `DlqDepthMonitor`, `DlqRecord` in Worker; 11 new tests, runbook | PR #10 merged to develop | ✅ |
| 10 | Transactional Outbox (M2.5) | `OutboxMessage` entity + filtered index + unique index; `KendoMessageSerializer`; `OutboxRelayService` BackgroundService; `OutboxRepository`; UsersController refactored; EF migration; 15 new tests | PR #11 merged to develop | ✅ |
| 11 | Async observability: traceparent propagation (M2.6) | `OutboxMessage.TraceContext` column; ambient `Activity.Current?.Id` capture; `traceparent` Rebus header; child Activity in Worker handler; EF migration; 17 new tests | PR #12 merged to develop | ✅ |
| 12 | Reverse proxy load balancer (M3.1) | `ops/nginx/nginx.conf`, `docker-compose.yml` NGINX + `expose:`, multi-replica CI, 5 infra tests, runbook | PR #14 squashed to `develop` | ✅ |
| 12 | Stateless validation & Redis (M3.2) | Redis distributed cache, replica identity middleware, X-Kendo-Replica header, sticky-session disable, 7 new tests (76 total) | PR #15 squashed to `develop` | ✅ |
| 13 | Chaos test suite (M3.3) | xUnit chaos tests (DB downtime, service crash, network partition), bash scripts, chaos-test CI job, runbook, 3 new test files | PR #16 merged to `develop` | ✅ |
| 14 | Rate limiting & load shedding (M3.4) | RateLimitingMiddleware (429 + Retry-After), LoadSheddingMiddleware (503 RFC 7807), 8 new tests (84 total), runbook | PR #17 squash-merged to `develop` | ✅ |
| 15 | Graceful shutdown (M3.5) | `Kendo.Shared.GracefulShutdown` module (RequestTracker, GracefulShutdownMiddleware, GracefulShutdownHostedService), wired into all 3 services, 13 new tests (97 total), runbook | PR #19 squash-merged to `develop` | ✅ |
| 16 | Ops runbooks (M3.6) | 4 scenario playbooks (db-failover, service-crash, dlq-drain, horizontal-scaling) + central index; chaos-test CI flakiness documented; 0 code changes; 97/97 tests passing | PR #20 squash-merged to `develop` | ✅ |
| 17 | Gateway JWT auth + FastAPI AI integration (M0.1+M0.2) | RsaKeyProvider, JwksEndpoint, AddKendoJwt, ServiceJwtMinter, AdminScopePolicies, IFastAPIClient, FastAPIClient (Polly timeout+retry+CB), RagController (W1/W2), UserSearchController (W3), AssistantController (W6), IntentAdvisoryMiddleware (W4); 22 files, 1363 additions; 22 new tests (119 total) | PR #22 merged to `develop` | ✅ |
| 18 | AI Event Contracts + Queue Topology (M0.3+M0.4) | KendoTopology constants, 4 AI event types (EventIngested, EventValidated, UserEmbeddingUpdated, NotificationRequested), AddKendoRebusAiConsumer, AddKendoRebusAiProducer, dual-queue DlqDepthMonitor, Program.cs wiring across 3 services; 12 files, 471 additions; 119/119 tests passing | PR #23 squash-merged to `develop` | ✅ |
| 19 | Worker AI Handlers + W7 Summarization (M0.5) | 4 new Rebus handlers (EventIngested, EventValidated, UserEmbeddingUpdated, NotificationRequested), IFastAPISummarizationClient with independent Polly (15s CB), NotificationDispatcherHostedService, service-JWT minter for Worker→UserService calls; 20 files, 1405 additions; 119/119 tests passing | PR #24 squash-merged to `develop` | ✅ |
| 20 | UserService Event Domain + Embeddings Foundation (M0.6) | 4 new entities (Event, EventValidation, EventEmbedding, UserEmbedding), 3 migrations, EventsController (5 routes), EmbeddingAdminController with admin:writes scope, scoped userservice_writer connection, startup hook; 18 files, 1328 additions; 121/121 tests passing | PR #26 squash-merged to `develop` | ✅ |
| 21 | FastAPI Service Scaffold (M5.1) | Python 3.12 FastAPI scaffold, health endpoints, JWT auth, RFC 7807, multi-stage Dockerfile, docker-compose integration; 15 files, 393 additions; no .NET changes | PR #27 squash-merged to `develop` | ✅ |

---

## Active Dependency Map
> Foundational resources introduced across days. Update "Consumed By" when a new service depends on a resource.

| Resource | Type | Purpose | Introduced | Consumed By |
|---|---|---|---|---|
| PostgreSQL | Database | Persistent state storage (pgvector enabled) | Day 00 | UserService |
| RabbitMQ | Message Broker | Async workflow decoupling | Day 00 | ~ |
| Redis | Cache / State | Distributed cache for stateless session state, replica identity | Day 00 | Gateway, UserService, Worker |
| Gateway | Web API | API routing, auth, rate limiting | Day 01 | Angular Web App, UserService |
| UserService | Web API | User domain logic, EF Core | Day 01 | Gateway |
| Worker | Background Service | Async processing, health endpoint, Rebus consumer | Day 01 | Rebus (ASB), Gateway, UserService |
| Rebus (ASB transport) | Message Bus | Azure Service Bus transport via Rebus, producer + consumer modes, `kendo-events` queue | Day 06 | Gateway (producer), UserService (producer), Worker (consumer) |
| IdempotencyRecords | DB table | Tracks processed messages by MessageId PK; enforces idempotency, enables crash recovery | Day 08 | Worker (UserCreatedEventHandler) |
| DlqRecords | DB table | Tracks dead-lettered messages from ASB DLQ; audit trail for manual recovery | Day 09 | Worker (DeadLetterHandler) |
| Dead Letter Queue | ASB DLQ (auto) | `kendo-events/$DeadLetterQueue` — auto-created by ASB for the main queue | Day 09 | Worker (DeadLetterHandler, DlqDepthMonitor) |
| OutboxMessages | DB table | Transactional outbox table for atomic event publishing; filtered index for unprocessed messages, unique index on MessageId | Day 10 | UserService (UsersController, OutboxRelayService) |
| AI Event Contracts | Messaging | 4 new KendoMessage types for AI workflows (EventIngestedEvent, EventValidatedEvent, UserEmbeddingUpdatedEvent, NotificationRequestedEvent) | Day 18 | Gateway (producer), UserService (producer), Worker (consumer + handlers) |
| AI Queue Topology | Messaging | `kendo-events-ai` queue with dedicated producer/consumer registrations, TypeBased routing, DLQ monitoring | Day 18 | Worker (AddKendoRebusAiConsumer), Gateway (AddKendoRebusAiProducer), UserService (AddKendoRebusAiProducer) |
| Events Table | DB table | `events` table with Event entity, FK to users, IngestionStatus tracking | Day 20 | UserService (EventsController), FastAPI (read-only, Phase 05) |
| Event Embeddings Table | DB table | `event_embeddings` sidecar table with pgvector vector column for W1 ingestion | Day 20 | FastAPI (read-only, Phase 05), UserService (EmbeddingAdminController) |
| User Embeddings Table | DB table | `user_embeddings` sidecar table with pgvector vector column for W3 semantic search | Day 20 | FastAPI (read-only, Phase 05), UserService (EmbeddingAdminController) |
| fastapi_ro Role | DB Role | PostgreSQL read-only role for FastAPI service; SELECT-only grants on events and embeddings | Day 20 | FastAPI (Phase 05) |
| userservice_writer Role | DB Role | PostgreSQL write role for EmbeddingAdminController; INSERT/UPDATE on embeddings | Day 20 | UserService (EmbeddingAdminController via scoped connection) |

---

## Active Infrastructure Snapshot
> Full replacement each session. Reflects current known state of all services, DBs, queues, pipelines.

* **Services:** Gateway (port 5000 behind NGINX), UserService (port 5001 expose), Worker (port 5002 expose), FastAPI (port 8000 internal), PostgreSQL (port 5432), Redis (port 6379), NGINX (port 80 internal, 5000 host) — all containerized
* **Docker Compose:** All 7 services with health checks; multi-replica (3 each) scaling for chaos testing
* **Chaos Test Suite:** 3 xUnit tests (`Category=Chaos`) with Docker CLI integration. 3 bash scripts in `scripts/chaos/`. CI job `chaos-test` runs after `docker-compose`, invokes `run_all.sh` on multi-replica stack, uploads structured results artifact
* **Tests:** 121/121 unit tests passing (no .NET changes this day; FastAPI Python scaffold verified with integration tests)
* **Runbooks:** Day 16 scenario playbooks + Day 17-21 day runbooks covering JWT auth, AI event contracts, Worker AI handlers, UserService Event Domain, and FastAPI scaffold operations
* **Branches:** `develop` (PR #27 squash-merged — Day 21: FastAPI Service Scaffold M5.1) — on `origin`
* **Pipelines:** CI pipeline active: build-and-test -> docker-compose -> chaos-test (known flakiness: test_db_downtime intermittent 000000)

* **Docker Compose:** All 7 services with health checks; PostgreSQL (5s interval), app services (10s interval), Redis (5s interval), NGINX (10s interval, wget self-health), FastAPI (10s interval, curl health/live); `depends_on` postgres healthy → userservice; fastapi depends_on postgres
* **Database:** PostgreSQL 16 + pgvector (`pgvector/pgvector:pg16`), `kendo_users` DB, `vector` extension enabled via EF Core migration
* **Messaging:** Rebus registered with Azure Service Bus transport — Gateway + UserService in producer mode (one-way client), Worker in consumer mode (polls `kendo-events`, 3 workers). Graceful skip when `Rebus__ConnectionString` is missing (local dev).
* **Idempotency:** `IdempotencyRecords` table (WorkerDbContext) tracks message processing status (Processing/Completed/Failed). MessageId PK enforces uniqueness. Crash recovery re-processes messages left in Processing state. All handlers wrap DB ops in transactions.
* **Branches:** `main` (scaffolding), `develop` (PR #27 squash-merged — Day 21: FastAPI Service Scaffold M5.1) — both on `origin`
* **Pipelines:** CI pipeline active — build → unit tests → data integration tests (with pgvector + Redis service containers) → resilience tests → messaging tests → docker compose health verification → replica header verification → traffic distribution check. AI queue topology tests (graceful skip when ASB absent) run as part of messaging tests.
* **Tests:** 121/121 unit tests passing (119 existing + Day 20 entity/DbContext/controller tests)
* **Observability:** All 3 services emit OpenTelemetry traces to console exporter; trace IDs correlated in all ILogger log lines; Polly callbacks emit structured logs with trace context; error responses include trace ID in RFC 7807 `traceId` field
* **Local:** API instances: 4 (Gateway, UserService, Worker, FastAPI), Postgres: 1 (Docker), RabbitMQ: 1 (infrastructure, not yet consumed), Redis: 1 (Docker, wired, best-effort cache)

---

## Architectural Decisions Log
> Permanent. Append only. API contract freezes and irreversible tech choices.

* **Resilience:** All internal synchronous HTTP calls use Circuit Breaker — 3-strike failure threshold; Polly policies on all DB clients and `HttpClient`.
* **Scale:** All stateless APIs designed for `replicas: 3`; no in-process session state permitted.
* **Messaging:** Rebus targeting Azure Service Bus for all event-driven patterns. *(Day 01)*
* **Framework:** .NET 10 MVC Controllers + EF Core + Data Annotations — no Minimal APIs.
* **Health Checks:** All services expose `/health/live` and `/health/ready` as `text/plain`; Docker health checks use curl. *(Day 01)*
* **Worker HTTP:** Worker Service embeds Kestrel via `Microsoft.NET.Sdk.Web` for health check endpoints, running `BackgroundService` alongside. *(Day 01)*
* **Solution Format:** Using `.slnx` (new .NET 10 solution format) instead of legacy `.sln`. *(Day 01)*
* **Database Provider:** Npgsql.EntityFrameworkCore.PostgreSQL for EF Core; connection string externalized via `ConnectionStrings__DefaultConnection` env var. *(Day 02)*
* **pgvector:** `vector` extension enabled in all EF Core migrations; `HasPostgresExtension("vector")` applied at model level. *(Day 02)*
* **Shared Library:** `Kendo.Shared` class library introduced — houses resilience pipeline, options, HTTP handler, and future shared concerns (RFC 7807 middleware, OpenTelemetry config). All services reference it. *(Day 03)*
* **Polly Resilience:** All DB and HTTP calls wrapped in Polly v8 Retry (3 attempts, exponential backoff + jitter) + Circuit Breaker (3 consecutive failures → 30s break). Config externalized via `Resilience` appsettings section. *(Day 03)*
* **OpenTelemetry:** All services emit OpenTelemetry traces to console exporter via `AddKendoObservability()` extension method in `Kendo.Shared`. Trace IDs auto-correlated in `ILogger` output via ASP.NET Core `Activity.Current.TraceId`. Console exporter for dev/CI; OTLP exporter conditionally switchable via `OTEL_EXPORTER_OTLP_ENDPOINT` in production. *(Day 04)*
* **CI PostgreSQL:** CI `build-and-test` job uses `pgvector/pgvector:pg16` service container for data integration tests. Connection string externalized via `ConnectionStrings__DefaultConnection` env var. *(Day 04)*
* **RFC 7807 Problem Details:** All 3 services return `application/problem+json` on all 4xx/5xx responses via shared `ProblemDetailsMiddleware` in `Kendo.Shared`. Exception→status mapping: `ArgumentException`→400, `KeyNotFoundException`→404, `OperationCanceledException`→503, generic→500, client-disconnect→499. Health endpoints exempt. *(Day 05)*
* **Async Messaging — Rebus:** Rebus `10.7.2` + `Rebus.AzureServiceBus` `10.7.0` added to all 3 services. Shared `AddKendoRebus()` extension in `Kendo.Shared.Messaging` with producer (one-way ASB client, auto-skip on missing connection string) and consumer (ASB queue `kendo-events`, 3 workers, 10 parallelism) modes. `KendoMessage` abstract record defines base message shape (`MessageId`, `CreatedAt`). *(Day 06)*
* **Idempotency — MessageId Key:** `KendoMessage.MessageId` used as idempotency key. `IdempotencyRecords` table in WorkerDbContext tracks Processing/Completed/Failed states. MessageId PK enforces unique constraint. Crash recovery re-processes Processing-state messages. *(Day 08)*
* **DLQ Consumer — DeadLetterQueue sub-queue:** ASB auto-creates `kendo-events/$DeadLetterQueue` for the main queue. `KendoRebusDlqConfiguration.AddKendoRebusDlqConsumer()` registers a separate Rebus consumer on this sub-queue with `NumberOfWorkers: 1`. The consumer is disabled by default (`Rebus__DlqConsumerEnabled: false`). `DeadLetterHandler` is fail-safe — acknowledges messages even if persistence fails, preventing re-delivery loops. *(Day 09)*
* **DLQ Depth Monitoring — Polling Monitor:** `DlqDepthMonitor` BackgroundService polls ASB management API at configurable interval (default 60s). Alert fires via `LogLevel.Error` when depth exceeds configurable threshold (default 5). Rate-limited dedup prevents alert spam. *(Day 09)*
* **Transactional Outbox — OutboxMessages table:** `OutboxMessage` entity in UserService's `AppDbContext` with filtered index `IX_OutboxMessages_Unprocessed` (WHERE ProcessedAt IS NULL) for relay polling and unique index `IX_OutboxMessages_MessageId` for deduplication. `OutboxRelayService` BackgroundService polls pending messages, publishes via Rebus `IBus.Send()`, and marks as processed. RetryCount + LastError fields enable failure tracking up to configurable MaxRetries (default: 5). *(Day 10)*
* **Traceparent Correlation — Cross-process trace linking:** `OutboxMessage.TraceContext` column (nullable text) captures `Activity.Current?.Id` at outbox write time. `OutboxRelayService` forwards it as `traceparent` Rebus message header. `UserCreatedEventHandler.StartTraceActivity()` extracts header and creates child `Activity` linked to the producer trace. Defensive parsing — malformed headers fall back to fresh trace. Best-effort correlation design; null TraceContext handled gracefully. *(Day 11)*
* **Distributed Cache — Redis / In-Memory Fallback:** `AddKendoDistributedCache()` extension in `Kendo.Shared.Caching` registers `StackExchangeRedis` when `Redis__ConnectionString` is set, falls back to `AddDistributedMemoryCache()` otherwise. Best-effort — cache miss on Redis returns null; no cascading failure. *(Day 12)*
* **Replica Identity — X-Kendo-Replica Header:** `ReplicaIdentityMiddleware` reads Docker `$HOSTNAME` (fallback `Environment.MachineName` → "unknown") and appends `X-Kendo-Replica` header to all HTTP responses. NGINX passes through via `proxy_set_header X-Kendo-Replica $upstream_http_x_kendo_replica;`. *(Day 12)*
* **Redis in Docker Compose:** `redis:7-alpine` with `redis-cli ping` health check. All 3 services receive `Redis__ConnectionString=redis:6379` env var. CI includes Redis service container. *(Day 12)*
* **Rate Limiting — Fixed-Window (Gateway):** `FixedWindowRateLimiter` registered as singleton in DI via `AddKendoRateLimiting()`. `RateLimitingMiddleware` enforces permit limit per time window at Gateway. Health endpoints bypassed. `System.Threading.RateLimiting` primitives used directly (no ASP.NET middleware package dependency). *(Day 14)*
* **Load Shedding — Concurrency Limiter (Gateway):** `ConcurrencyLimiter` registered as singleton in DI. `LoadSheddingMiddleware` returns 503 RFC 7807 under extreme concurrency. Runs before rate limiter in middleware pipeline. Health endpoints bypassed. *(Day 14)*
* **Graceful Shutdown — SIGTERM + Drain (All Services):** `Kendo.Shared.GracefulShutdown` module with `RequestTracker` (thread-safe in-flight counter), `GracefulShutdownMiddleware` (blocks new requests during drain with 503 RFC 7807, health endpoints bypassed), and `GracefulShutdownHostedService` (triggers drain on `StopAsync()` and waits for completion). Configurable via `GracefulShutdown__TimeoutSeconds` (default 30s). ASP.NET Core `HostOptions.ShutdownTimeout` wired via `AddKendoGracefulShutdown()`. *(Day 15)*
* **Worker FastAPI Summarization Client — Independent Polly Pipeline:** The Worker's `FastAPISummarizationClient` uses its own Polly pipeline (15s timeout, 3-failure → 30s circuit breaker) independent from the Gateway's `IFastAPIClient`. This ensures W7 (summarization) cannot starve W1/W2 (ingest/validate) of circuit breaker capacity. *(Day 19)*
* **UserService Event Domain — Sidecar Embeddings + pgvector Hybrid:** Event, EventValidation, EventEmbedding, and UserEmbedding entities stored as sidecar tables (not columns on existing tables) to keep existing schemas immutable. Embedding columns use pgvector `vector` type via raw SQL ALTER in migration. Environment-driven dimension enforcement via `kendo.embedding_dim` GUC set on startup. *(Day 20)*
* **Admin Scope — service-JWT-only enforcement:** `admin:writes` authorization policy enforces both `scope: admin:writes` AND `token_use: service` claims, preventing user JWTs from accessing admin endpoints. Defense-in-depth: JWT scope check + separate `userservice_writer` PostgreSQL role with scoped connection. *(Day 20)*
* **FastAPI Service — Python 3.12 + FastAPI:** First non-.NET workload in the platform. Uses `fastapi[standard]`, `pydantic-settings` for env-based config. App factory pattern (`create_app()`). JWT auth middleware (RS256, health endpoint exemption). RFC 7807 Problem Details exception handler matching .NET convention. Multi-stage Dockerfile with `python:3.12-slim`. Docker Compose integration: internal port 8000, health check, depends_on postgres. *(Day 21)*
