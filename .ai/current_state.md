# Infraspekt — Current State
> **Live checkpoint.** Updated by the Orchestrator at the end of every phase.  
> Rule: never truncate history. Append only — except `## Last Session Summary` and `## Active Infrastructure Snapshot` (full replacements).  
> Last updated: 2026-06-13

---

## Session Variables

```yaml
current_day: 3
current_phase: 4b       # 0=Init · 0b=Bootstrap · 1=Architect · 2=Dev+QA · 4=DevOps · 4b=Review · 5=State Update
branch_base: develop
feature_branch: ~       # resolved in Phase 1 from {{SLUG}}
phase_plan: "01"        # platform_roadmap.md phase reference
```

---

## Last Session Summary
> Replaced each session. 3-bullet hand-off note for the next run.

* M1.2 database layer delivered: PostgreSQL 16 + pgvector provisioned, EF Core DbContext wired, initial migration applied, connection string externalized.
* 12/12 xUnit tests passing; 4 Docker Compose services healthy (Gateway, UserService, Worker, PostgreSQL).
* Next: M1.3 — Resilience baseline (Polly Circuit Breaker + Retry on all DB and HTTP clients). Carry-forward: RFC 7807 error schema (target M1.5).

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
| 4b | `docs/architecture/day_03_review_report.md` + PR merged to `develop` | ~ |
| 5 | `changelog/YYYY-MM-DD.md` entry · `validate_state.sh` exit 0 | ~ |

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
* **Branches:** `main` (scaffolding), `develop` (PR #3 squash-merged — Day 02) — both on `origin`
* **Pipelines:** CI pipeline active (`.github/workflows/ci.yml`) — build → unit tests → data integration tests → docker compose health verification
* **Tests:** 12/12 xUnit tests passing (8 health-check + 4 data integration)
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
