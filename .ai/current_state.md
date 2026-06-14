# Infraspekt — Current State
> **Live checkpoint.** Updated by the Orchestrator at the end of every phase.  
> Rule: never truncate history. Append only — except `## Last Session Summary` and `## Active Infrastructure Snapshot` (full replacements).  
> Last updated: 2026-06-13 (Day 09)

---

## Session Variables

```yaml
current_day: 9
current_phase: 0        # 0=Init · 0b=Bootstrap · 1=Architect · 2=Dev+QA · 4=DevOps · 4b=Review · 5=State Update
branch_base: develop
feature_branch: ~       # resolved in Phase 1 from {{SLUG}}
phase_plan: "02"        # platform_roadmap.md phase reference
```

---

## Last Session Summary
> Replaced each session. 3-bullet hand-off note for the next run.

* M2.4 — Dead Letter Queue consumer and alerting: `DeadLetterHandler` consumes DLQ messages with fail-safe persistence; `DlqDepthMonitor` periodically polls ASB DLQ depth and fires `LogLevel.Error` alert when configurable threshold (default: 5) is exceeded; rate-limited alert dedup prevents log spam.
* 47/47 unit tests passing (36 existing + 11 new DLQ tests); PR #10 squash-merged into `develop`.
* Next: M2.5 — Transactional Outbox pattern. Phase 02 continues. Carry-forward: none.

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

*(none — all items resolved)*

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

---

## Active Dependency Map
> Foundational resources introduced across days. Update "Consumed By" when a new service depends on a resource.

| Resource | Type | Purpose | Introduced | Consumed By |
|---|---|---|---|---|
| PostgreSQL | Database | Persistent state storage (pgvector enabled) | Day 00 | UserService |
| RabbitMQ | Message Broker | Async workflow decoupling | Day 00 | ~ |
| Redis | Cache / State | Distributed circuit breaker state | Day 00 | ~ |
| Gateway | Web API | API routing, auth, rate limiting | Day 01 | Angular Web App, UserService |
| UserService | Web API | User domain logic, EF Core | Day 01 | Gateway |
| Worker | Background Service | Async processing, health endpoint, Rebus consumer | Day 01 | Rebus (ASB), Gateway, UserService |
| Rebus (ASB transport) | Message Bus | Azure Service Bus transport via Rebus, producer + consumer modes, `kendo-events` queue | Day 06 | Gateway (producer), UserService (producer), Worker (consumer) |
| IdempotencyRecords | DB table | Tracks processed messages by MessageId PK; enforces idempotency, enables crash recovery | Day 08 | Worker (UserCreatedEventHandler) |
| DlqRecords | DB table | Tracks dead-lettered messages from ASB DLQ; audit trail for manual recovery | Day 09 | Worker (DeadLetterHandler) |
| Dead Letter Queue | ASB DLQ (auto) | `kendo-events/$DeadLetterQueue` — auto-created by ASB for the main queue | Day 09 | Worker (DeadLetterHandler, DlqDepthMonitor) |

---

## Active Infrastructure Snapshot
> Full replacement each session. Reflects current known state of all services, DBs, queues, pipelines.

* **Services:** Gateway (port 5000), UserService (port 5001), Worker (port 5002), PostgreSQL (port 5432) — all containerized
* **Docker Compose:** All four services with health checks; PostgreSQL (5s interval), app services (10s interval); `depends_on` postgres healthy → userservice
* **Database:** PostgreSQL 16 + pgvector (`pgvector/pgvector:pg16`), `kendo_users` DB, `vector` extension enabled via EF Core migration
* **Messaging:** Rebus registered with Azure Service Bus transport — Gateway + UserService in producer mode (one-way client), Worker in consumer mode (polls `kendo-events`, 3 workers). Graceful skip when `Rebus__ConnectionString` is missing (local dev).
* **Idempotency:** `IdempotencyRecords` table (WorkerDbContext) tracks message processing status (Processing/Completed/Failed). MessageId PK enforces uniqueness. Crash recovery re-processes messages left in Processing state. All handlers wrap DB ops in transactions.
* **Branches:** `main` (scaffolding), `develop` (PR #10 squash-merged — Day 09) — both on `origin`
* **Pipelines:** CI pipeline active (`.github/workflows/ci.yml`) — build → unit tests → data integration tests (with pgvector service container) → resilience tests → **messaging tests** → docker compose health verification. CI `Wait for healthy` step hardened to wait for all 4 services.
* **Tests:** 47/47 unit tests passing (8 health-check + 4 data integration + 13 resilience + 4 observability + 10 ProblemDetails middleware + 4 Messaging registration + 7 Worker handler idempotency + 4 DeadLetterHandler + 7 DlqDepthMonitor)
* **Observability:** All 3 services emit OpenTelemetry traces to console exporter; trace IDs correlated in all ILogger log lines; Polly callbacks emit structured logs with trace context; error responses include trace ID in RFC 7807 `traceId` field
* **Local:** API instances: 3 (Gateway, UserService, Worker), Postgres: 1 (Docker), RabbitMQ: 1 (infrastructure, not yet consumed), Redis: 1 (infrastructure)

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
