# Infraspekt — Current State
> **Live checkpoint.** Updated by the Orchestrator at the end of every phase.  
> Rule: never truncate history. Append only — except `## Last Session Summary` and `## Active Infrastructure Snapshot` (full replacements).  
> Last updated: 2026-06-13 (Day 04)

---

## Session Variables

```yaml
current_day: 5
current_phase: 0        # 0=Init · 0b=Bootstrap · 1=Architect · 2=Dev+QA · 4=DevOps · 4b=Review · 5=State Update
branch_base: develop
feature_branch: ~       # resolved in Phase 1 from {{SLUG}}
phase_plan: "01"        # platform_roadmap.md phase reference
```

---

## Last Session Summary
> Replaced each session. 3-bullet hand-off note for the next run.

* M1.4 observability foundation delivered: OpenTelemetry console exporter, trace-ID correlation in all logs, structured Polly callback logging across all services. CI PostgreSQL container resolved.
* 29/29 xUnit tests passing (8 unit + 4 data + 13 resilience + 4 observability); CI `build-and-test` job now has pgvector service container.
* Next: M1.5 — RFC 7807 Problem Details error schema (standardized error responses). Carry-forward: none new.

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

- Define global RFC 7807 error schema for the API layer. *(carried from Day 00, target: M1.5)*

---

## Completed Days

| Day | Title | Key Outputs | Notes | Status |
|---|---|---|---|---|
| 00 | Repo Scaffolding | `.ai/` structure, GitHub Actions baseline | Phase 0b bootstrap committed | ✅ |
| 01 | Multi-service scaffold | Gateway + UserService + Worker, Docker Compose, CI pipeline, 8 tests | PR #2 merged to develop | ✅ |
| 02 | Database layer | PostgreSQL 16 + pgvector, EF Core DbContext, initial migration, connection string externalized, 12 tests | PR #3 merged to develop | ✅ |
| 03 | Resilience baseline | Polly Retry + Circuit Breaker on DB + HTTP clients, shared resilience pipeline, 25 tests (13 resilience), CI updated | PR #4 merged to develop | ✅ |
| 04 | Observability foundation | OpenTelemetry console exporter, trace-ID correlation, structured Polly logging, CI PostgreSQL fix, 29 tests (4 observability) | PR #5 merged to develop | ✅ |

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
| Worker | Background Service | Async processing, health endpoint | Day 01 | Rebus (future) |

---

## Active Infrastructure Snapshot
> Full replacement each session. Reflects current known state of all services, DBs, queues, pipelines.

* **Services:** Gateway (port 5000), UserService (port 5001), Worker (port 5002), PostgreSQL (port 5432) — all containerized
* **Docker Compose:** All four services with health checks; PostgreSQL (5s interval), app services (10s interval); `depends_on` postgres healthy → userservice
* **Database:** PostgreSQL 16 + pgvector (`pgvector/pgvector:pg16`), `kendo_users` DB, `vector` extension enabled via EF Core migration
* **Branches:** `main` (scaffolding), `develop` (PR #5 squash-merged — Day 04) — both on `origin`
* **Pipelines:** CI pipeline active (`.github/workflows/ci.yml`) — build → unit tests → data integration tests (with pgvector service container) → resilience tests → docker compose health verification. OTel observability tests added to suite.
* **Tests:** 29/29 xUnit tests passing (8 health-check + 4 data integration + 13 resilience + 4 observability)
* **Observability:** All 3 services emit OpenTelemetry traces to console exporter; trace IDs correlated in all ILogger log lines; Polly callbacks emit structured logs with trace context
* **Local:** API instances: 3 (Gateway, UserService, Worker), Postgres: 1 (Docker), RabbitMQ: 1 (infrastructure), Redis: 1 (infrastructure)

---

## Architectural Decisions Log
> Permanent. Append only. API contract freezes and irreversible tech choices.

* **Resilience:** All internal synchronous HTTP calls use Circuit Breaker — 3-strike failure threshold; Polly policies on all DB clients and `HttpClient`.
* **Scale:** All stateless APIs designed for `replicas: 3`; no in-process session state permitted.
* **Messaging:** Rebus targeting Azure Service Bus for all event-driven patterns.
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
