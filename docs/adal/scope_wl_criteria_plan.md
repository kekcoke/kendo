> **Date:** 2026-06-20  
> **POC:** Kendo Orchestrator (Orchestrator persona)  
> **TL;DR:** Finalized spec-generation plan for Phase 05 workload sub-milestones M5.7–M5.13, incorporating user decisions on ordering, carry-forward resolution, spec granularity, and deployment timing.

---

# Session Plan — Workload Criteria Scope (M5.7–M5.13)

## Context

- **Phase plan:** `05` — AI/Vector Service (FastAPI)
- **Current day:** `26` (M5.6 sealed on Day 25)
- **Foundation sealed:** M5.1–M5.6 all ✅ — FastAPI scaffold, LangChain RAG pipeline, pgvector read-only, Gateway routing, resilience parity (pybreaker + tenacity), observability/chaos/runbook
- **No pre-authored spec files exist** for any M5.7–M5.13 workload — every milestone requires standard Phase 1 generation per `entrypoint.md`
- **Authoritative contract source:** `docs/architecture/fastapi_rag_service_spec.md §Workload Catalog` — all data contracts, acceptance gates, and dependency maps are defined there

---

## User Decisions (applied)

| Question | Decision | Rationale |
|---|---|---|
| Q1 — M5.14 (W8) in scope? | **Leave as prerequisite dependency** — noted in M5.7's Phase 4b gate, not spec-generated here | Keeps M5.7–M5.13 scope focused on workloads; W8 is infrastructure that must exist before W1 ships, not a workload itself |
| Q2 — Carry-forward approach | **Proposal A** with deployment-timing notes added | Dedicated days for CF-1 and CF-2 before any workload work |
| Q3 — Spec granularity | **Adopt by reference** (see §Explanation below) | Single source of truth, avoids duplication, matches established pattern |
| Q4 — P1/P2 ordering | **W5 first** (embeddings backfill before semantic search) | W5 sets up the embedding infrastructure that W3 reads; clean dependency chain |

---

## Spec Granularity — Explanation (Q3)

Two approaches for how each Phase 1 spec references the workload contracts in `fastapi_rag_service_spec.md`:

### Option 1: Adopt by reference (✅ Chosen)

Each day spec's `## Data Contracts` and `## Implementation Plan` sections **cite** the relevant Workload Catalog section rather than copy-pasting. For example, a Day 29 (M5.7) spec would say:

> **Data contracts:** See `fastapi_rag_service_spec.md §W1 — Event Ingestion RAG` for request/response schemas, acceptance gates, and the LLM chain contract. The spec below defines only the commit-unit breakdown and file-level implementation details specific to this day.

**Benefits:**
- **Single source of truth:** Updates to `fastapi_rag_service_spec.md` propagate without editing N specs
- **Lighter specs:** Each day spec focuses on what's unique — the commit units, file paths, gate commands, and integration points
- **Established pattern:** The `orchestration.md §9 Spec Taxonomy` distinguishes "design specs" (hand-authored, like `fastapi_rag_service_spec.md`) from "kickstarter specs" (Phase 1 output, like day specs). Kickstarter specs cite design specs via `## Depends on`, not restatement
- **Prior art:** Day 22 spec already adopted `fastapi_rag_service_spec.md` by reference for the M5.2+M5.3 work

**Risk:** If the design spec is updated after a day spec is generated, the day spec references a contract that no longer matches. **Mitigation:** Each day spec's `## Milestone Scope` pins the exact section and revision date it references. Phase 4b review verifies the reference is still current.

### Option 2: Restate from scratch (rejected)

Each day spec copies the data contracts, acceptance criteria, and resilience mandates verbatim into its own sections.

**Drawbacks:**
- Duplication across 7 specs — updates to `fastapi_rag_service_spec.md` require editing all 7
- Heavier specs, more token cost in Phase 2 (Developer reads bloated context)
- Risk of silent drift: one spec gets updated but others don't

---

## Carry-Forward Items (from current_state.md)

Two carry-forward items must be addressed before any workload implementation begins:

| ID | Item | Source | Risk if unresolved |
|---|---|---|---|
| **CF-1** | `test_db_downtime` chaos test intermittently returns `000000` instead of expected 503. Documented in `ops/runbooks/fastapi_service.md` §Known CI Flakiness. Mitigation #2 (retry loop after `docker compose unpause`) recommended. | M3.3 (Day 13) / M3.6 (Day 16) | Erodes CI trust. Each new workload (M5.7+) adds its own chaos tests; flaky baseline makes regressions unreadable. |
| **CF-2** | Day 17 open questions: `IssuerSigningKeyResolver` anti-pattern (`BuildServiceProvider()`), no user JWT issuance (validation only), no key rotation `BackgroundService` (manual only). | M0.1/M0.2 (Day 17) | FastAPI JWT validation depends on the Gateway's JWKS endpoint contract. If the key resolver is refactored after W1 is live, the JWKS schema could change, breaking FastAPI auth. Must resolve before M5.7 ships. |

---

## Milestone Mapping — M5.7 to M5.13

Reference: `fastapi_rag_service_spec.md §Workload Catalog` + `platform_roadmap.md §Phase 05 — Workload sub-milestones`

| Milestone | Workload | Priority | Depends On | Acceptance Gate (from spec catalog) |
|---|---|---|---|---|
| **M5.7** | W1 — Event Ingestion RAG | **P0** | Foundation + M5.4 + CF-1 + CF-2 | `POST /api/events/ingest` → structured Event JSON; p95 ≤ 8s; grounded rate ≥ 90% |
| **M5.8** | W2 — Event Conflict & Schedule Reasoning | **P0** | W1 live (pgvector has event embeddings) | Zero false-negatives on conflict set; FP ≤ 5%; reasoning trace returned |
| **M5.11** | W5 — Embeddings Backfill & Re-indexing | P1 | M5.2, M5.3 + UserService admin write endpoint | 10k events ≤ 5min; idempotent; resumable |
| **M5.9** | W3 — User Profile Semantic Search | P1 | M5.2, M5.3 + user_embeddings migration (setup by W5) | precision@10 ≥ 0.85; p95 ≤ 300ms |
| **M5.10** | W4 — User Intent Classification | P1 | M5.1, M5.4, M5.5 | p95 ≤ 80ms; accuracy ≥ 95%; hard-coded fallback in Gateway |
| **M5.12** | W6 — Document Q&A / Onboarding Assistant | P2 | M5.1, M5.2, M5.3, M5.5, M5.6 | Citation accuracy ≥ 95%; faithfulness ≥ 0.9; p95 ≤ 4s |
| **M5.13** | W7 — Event Notification Summarization | P2 | M5.5, M5.6 + new IFastAPIClient in Kendo.Shared | e2e latency p95 ≤ 6s; tone control; prompt-version stored; RFC 7807 on failure |

---

## Deployment Timing —  develop  →  main  Merge Gate (Q2 addition)

When can the phase 05 work safely merge from `develop` to `main`? Two thresholds:

### Earliest Merge — Minimum Viable Phase 05 on `main`

**State:** CF-1 + CF-2 resolved → M5.7 (W1) shipped and stable on `develop`

**Prerequisites satisfied:**
- [ ] CF-1 resolved — CI chaos tests are stable (no flaky baseline)
- [ ] CF-2 resolved — Gateway JWKS contract frozen; user JWT issuance works; key rotation has a BackgroundService
- [ ] M5.14 (W8) eval gate running in CI (not part of this spec-gen run, but required before M5.7 ships to prod)
- [ ] M5.7 (W1) — first P0 workload, delivers user-visible value: free-form text → structured Event
- [ ] Phase 01–05 foundation criteria all still passing (regression guard from `orchestration.md`)

**Result:** `develop` has a complete, tested, eval-gated, value-delivering Phase 05 on top of the sealed foundation. Merge to `main` is safe. P1/P2 workloads continue on `develop` and ship as separate PRs post-merge.

**Estimated timeline:** After Day 29–30 (CF-1 + CF-2 + M5.7 completion, assuming M5.14 already exists on `develop` from a prior or parallel session).

### Latest Merge — Before drift becomes dangerous

**State:** If Phase 06 (whatever comes next — Observability Hardening per roadmap note) touches any of:
- Gateway auth middleware
- Docker Compose service definitions
- CI pipeline (build-and-test, chaos-test, or eval jobs)
- Shared data contracts (`Kendo.Shared`, message schemas)
- PostgreSQL migrations or roles

...then continued divergence between `develop` and `main` risks merge conflicts and behavioral drift. The **latest safe merge point** is **before Phase 06 starts**, regardless of how many P1/P2 workloads remain.

**Practical deadline:** After M5.8 (W2) ships — both P0 workloads are live. The P1/P2 workloads (W5, W3, W4, W6, W7) can ship as independent feature-branch PRs against `develop` after the main merge, without blocking the next phase.

**Result:** Merge after Day 31 (CF-1 + CF-2 + M5.7 + M5.8) even if no P1/P2 work has started yet. This is the **recommended merge window**: `develop` has both P0 workloads, eval gate is live, and Phase 06 can start on a merged baseline.

### Not-Recommended: Postpone past P1/P2

Merging only after all 7 workloads ship (~Day 37) risks:
- Phase 06 blocked for ~1 week waiting on P2 workloads that have no dependency on the main merge
- Accumulated merge complexity from 7 feature branches
- `main` diverges significantly from `develop` in CI config, Docker Compose, and possibly shared contracts

---

## Final Sequence — Proposal A (with W5 reorder)

```
Day | Milestone | Scope | Earliest main-merge? | Latest safe main-merge?
----|-----------|-------|----------------------|------------------------
26  | CF-1      | Fix chaos-test flakiness (mitigation #2) | No (prerequisite)     | —
27  | CF-2      | Refactor IssuerSigningKeyResolver + JWT issuance + rotation BackgroundService | No (prerequisite) | —
28  | M5.7      | W1 — Event Ingestion RAG (first P0 workload) | ✅ Yes — earliest viable | —
29  | M5.8      | W2 — Event Conflict & Schedule Reasoning (second P0) | ✅ Yes | ✅ **Recommended** — merge here
30  | M5.11     | W5 — Embeddings Backfill & Re-indexing (P1, infra-first) | Post-merge | Post-merge
31  | M5.9      | W3 — User Profile Semantic Search (P1, consumes W5 infra) | Post-merge | Post-merge
32  | M5.10     | W4 — User Intent Classification (P1) | Post-merge | Post-merge
33  | M5.12     | W6 — Document Q&A Onboarding Assistant (P2) | Post-merge | Post-merge
34  | M5.13     | W7 — Event Notification Summarization (P2) | Post-merge | Post-merge
```

### Execution Rules
1. **Each row = one spec file** via standard Phase 1 generation per `entrypoint.md`
2. **Phases execute serially** per `orchestration.md §4`: 0 → 1 → 2 → 4 → 4b → 5
3. **CF-1 resolved before any workload chaos tests land** — enforced at Phase 0 gate for Day 28+
4. **CF-2 resolved before M5.7 Phase 4b** — enforced at M5.7's Phase 4b review gate (Gateway JWKS must be stable)
5. **M5.14 (W8) eval gate must be live on `develop` before M5.7 ships to prod** — noted as a hard prerequisite, not generated here. If W8 isn't yet on `develop` when M5.7 reaches Phase 4b, the gate halts.
6. **P1/P2 workloads may ship post-main-merge** — Day 29 (after M5.8) is the recommended merge window. Post-merge, each workload ships as its own feature-branch PR against `develop`.

---

## Spec File Naming Convention

Per the Artifact Registry (`orchestration.md §8`):

| Order | Milestone | Spec path | Notes |
|---|---|---|---|
| 1 | CF-1 fix | `docs/architecture/day_26_spec.md` | DevOps-style: fix chaos script |
| 2 | CF-2 fix | `docs/architecture/day_27_spec.md` | .NET auth refactor (Gateway) |
| 3 | M5.7 (W1) | `docs/architecture/day_28_spec.md` | First P0 workload |
| 4 | M5.8 (W2) | `docs/architecture/day_29_spec.md` | Second P0 workload |
| 5 | M5.11 (W5) | `docs/architecture/day_30_spec.md` | P1 — embeddings backfill infra (first P1) |
| 6 | M5.9 (W3) | `docs/architecture/day_31_spec.md` | P1 — semantic search (consumes W5 infra) |
| 7 | M5.10 (W4) | `docs/architecture/day_32_spec.md` | P1 — intent classification |
| 8 | M5.12 (W6) | `docs/architecture/day_33_spec.md` | P2 — doc Q&A onboarding |
| 9 | M5.13 (W7) | `docs/architecture/day_34_spec.md` | P2 — notification summarization |

> Day numbers assume each milestone fits in one day. If Phase 4b review finds issues (returning to Phase 1 per `orchestration.md §4b FAIL verdict`), the day increments. The sequence is fixed; the day numbering shifts by the number of days spent on each milestone.

---

## Confirmations Needed

- [ ] **Spec granularity:** Adopt by reference (`fastapi_rag_service_spec.md §W<X>` cited in each day spec's `## Data Contracts`, with commit units and integration points defined fresh)
- [ ] **P1 order:** W5 → W3 → W4 (embeddings infrastructure before semantic search before classification)
- [ ] **Deploy window:** Merge to `main` recommended after M5.8 (Day 29), with P1/P2 shipping post-merge on `develop`
- [ ] **W8 prerequisite:** M5.7's Phase 4b gate must verify M5.14 (W8) eval gate is live on `develop` — this is a hard stop not generated by this plan

Once confirmed, I'll begin executing Phase 0 for Day 26 (CF-1 fix).
