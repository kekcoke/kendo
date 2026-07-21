# Kendo: An AI-Driven Software Factory — Architecture & Key Decisions

## The Problem

Shipping AI-augmented features on a production microservice platform is slow and risky without systematic discipline. Manual handoffs between spec → implement → review → deploy lose context. Infrastructure failures silently degrade. LLM workloads introduce non-deterministic behavior that traditional retry and circuit-breaker patterns weren't designed for. Teams spend 40%+ of sprint time on integration debt and incidents that shouldn't have been surprises.

## The Solution

**Kendo** is a virtual software factory combining a resilient polyglot microservice platform (.NET 10 + Python 3.12) with a gated, agent-orchestrated delivery pipeline that treats every feature as a factory-processed unit — from scope selection through architecture, implementation, validation, operations, review, and state update.

---

## Architecture at a Glance

The platform's architecture flows through five layers:

```
External → NGINX (≥3 replicas, health-check routing)
  → API Gateway (.NET 10: JWT auth, rate limiting, Polly CB+Retry)
    → UserService (.NET 10: EF Core, pgvector, Semantic Kernel RAG)
    → FastAPI Service (Python 3.12: LangChain RAG, SSE streaming, pybreaker+tenacity)
  → Azure Service Bus (via Rebus: outbox pattern, DLQ alerting)
    → Worker (.NET 10: Rebus consumer, idempotent handlers, notification dispatch)
  → PostgreSQL 16 + pgvector (single source of truth)
  → Redis 7 (distributed cache)
```

**Key points:**
- The **Gateway** is the sole public entry point. It handles authentication, rate limiting (429 + Retry-After), load shedding (503 RFC 7807), and proxies to internal services with independent Polly pipelines.
- **UserService** handles user lifecycle, event persistence, and synchronous RAG via Semantic Kernel. Embeddings live in pgvector sidecar tables — separate from the entity tables, keeping existing schemas immutable.
- **FastAPI (Python)** is the offload target for heavy/streaming LLM workloads — RAG chains, intent classification, event ingestion, notification summarization. Connectes as a read-only role (`fastapi_ro`) enforced at the database level.
- **Worker** consumes from Azure Service Bus via Rebus, with idempotency keys, DLQ monitoring, and a transactional outbox pattern for atomic event publishing.

---

## 8 Key Architectural Decisions (with Rationale)

### 1. Polyglot by Design
.NET 10 for transactional reliability (type system, DI, mature async) + Python 3.12 for AI/LLM workloads (LangChain, FastAPI ecosystem). Boundary: typed HTTP contracts with separate resilience policies.

### 2. pgvector over Standalone Vector DB
Most embeddings live alongside relational data (users, events, notifications). A standalone vector DB introduces distributed transaction problems. pgvector inside PostgreSQL 16 gives ACID consistency for the entity↔embedding relationship with zero additional operational surface. Tradeoff point: ~1M vectors.

### 3. Per-Dependency Circuit Breakers
Independent pybreaker instances for pgvector and Azure OpenAI. One dependency failing cannot trip the other. Without this, a pgvector maintenance window would block all LLM operations.

### 4. Read-Only DB Role (defense-in-depth)
FastAPI connects as `fastapi_ro` — SELECT-only grants at the database level. Even a bug or misconfiguration can't write to the database. The `userservice_writer` role used by admin endpoints requires a scoped connection string.

### 5. Independent Polly Pipelines
Gateway→FastAPI and Worker→FastAPI have separate circuit breakers. A spike in notification summarization traffic can trip the Worker's breaker without affecting Gateway-routed RAG queries. Learned the hard way after Phase 01's shared pipeline.

### 6. Three-Pillar Async Reliability
- **Transactional Outbox** — events persisted atomically with the DB transaction. Relay publishes to ASB with retry (max 5) before manual review.
- **Idempotency Keys** — `MessageId` PK on `IdempotencyRecords` table. Crash recovery re-processes messages left in `Processing` state.
- **Traceparent Propagation** — W3C trace context captured at outbox write, forwarded as Rebus header, extracted in Worker to link producer→consumer spans.

### 7. Chaos-in-CI
Every PR touching infra code runs 3 chaos scenarios (DB downtime, service crash, network partition) + bash Docker-level chaos. Pipeline fails on any regression. This is what makes the "3-replica, zero-downtime" claim testable, not aspirational.

### 8. RFC 7807 Everywhere
All 4xx/5xx responses across all 4 services return `application/problem+json` with `trace_id` from the active OpenTelemetry span. No raw exceptions cross the network boundary.

---

## The Orchestration Loop (6 Phases, Hard Gates)

Think of it as CI/CD for feature design, not just deployment.

| Phase | Agent | Output |
|-------|-------|--------|
| **0 — State Init & Scope** | Orchestrator | Dependency-aware milestone selection, variable resolution |
| **1 — Architecture & Contracts** | Platform Architect | `day_N_spec.md` with data contracts, commit plan, resilience mandate |
| **2 — Implementation + Validation** | Developer + QA (paired) | Per-commit-unit implementation + tests (gated: lint+test must pass before commit) |
| **4 — Delivery & Operations** | DevOps/SRE | CI pipeline, Dockerfile, `day_N_runbook.md` with rollback plan |
| **4b — Review & Merge** | Reviewer | PASS → merge to `develop`. **FAIL → discard ALL artifacts, restart from Phase 1** |
| **5 — State Update & Changelog** | Orchestrator | Append-only state tracking, changelog, `validate_state.sh` gate |

**Gate flow:** Phase 0 → [Phase 0b] → Phase 1 → Phase 2 → Phase 4 → Phase 4b → (FAIL→Phase 1) or (PASS→Phase 5)

**No phase may be skipped. Phase 0b (branch bootstrap) is the only conditional phase.**

---

## Key Orchestration Loop Evolutions

The loop evolved through experience over 35 days:

1. **Dependency-aware milestone ordering (Day 32):** Instead of picking the first undone row, Phase 0 scans all candidates and picks the one with satisfied dependencies. This lets W5 (embeddings backfill) ship before W3 (semantic search) when W3 depends on `user_embeddings` that W5 creates.

2. **Prior-day sealing gate (Day 32):** Phase 0 now verifies the previous session sealed properly — `validate_state.sh` exits 0, `incomplete_tasks` is empty. A failing seal blocks day advancement.

3. **Phase 4b FAIL = hard restart:** Discards ALL artifacts (spec, branch, runbook) and restarts from Phase 1. Architectural defects are caught at the architecture layer, not compensated at the implementation layer.

4. **Documentation gap scan (non-blocking):** Missing review reports/runbooks are surfaced as `mild` carry-forward items — visible technical debt that doesn't block forward progress.

---

## Results: 35 Days, 8 Phases, All Milestones Shipped

| Phase | Status | Key Components |
|-------|--------|---------------|
| 01 — Core APIs & Sync Resilience | ✅ Complete | Gateway, UserService, Worker, Polly, OTel, RFC 7807, pgvector |
| 02 — Async Decoupling | ✅ Complete | Rebus + ASB, DLQ, outbox, idempotency, traceparent |
| 03 — High Availability & Chaos | ✅ Complete | NGINX LB, Redis, chaos suite, rate limiting, graceful shutdown |
| 04 — Pre-FastAPI Reconciliation | ✅ Complete | JWT auth, AI event contracts, queue isolation, Event domain |
| 05 — AI/Vector Service | ✅ Complete | All 14 milestones (W1–W8) live on `develop` |

**Test coverage:** ~380 tests (.NET unit/data/resilience/messaging + FastAPI unit/chaos + W8 Eval Gate)

**CI pipeline:** build → test → docker (multi-replica health) → chaos → eval-gate (DeepEval + Ragas, fails on >5% regression)

---

## What's Next

- **Phase 06** — Production readiness: README cleanup, CI hardening, documentation gap closure
- **Phase 07** — Angular GUI for platform management
- **Phase 08** — Hetzner deployment with Terraform infrastructure-as-code

---

*Kendo is open source. Full source code, architecture specs (35+ days), runbooks, and chaos test suite available in the repository.*

🌸 Generated with [AdaL](https://github.com/adal-cli/)
