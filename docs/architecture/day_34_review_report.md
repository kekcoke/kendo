# Review Report — Day 34 (M5.12): W6 Document Q&A / Onboarding Assistant

> **Reviewer:** Kendo Reviewer Agent  
> **Date:** 2026-06-20  
> **Verdict:** ✅ PASS  
> **PR:** [#41](https://github.com/kekcoke/kendo/pull/41) — squash-merged to `develop` at `23ff73d`

---

## Gate Verification

| Gate | Status | Notes |
|---|---|---|
| Phase 1 — Architecture Spec | ✅ | `docs/architecture/day_33_spec.md` exists, adopts `fastapi_rag_service_spec.md §W6` by reference |
| Phase 2 — Commit Log | ✅ | 2/2 committed, zero halted |
| Phase 2 — Unit Tests | ✅ | 388 lines of tests — citation accuracy ≥ 95%, faithfulness ≥ 0.9, latency p95 ≤ 4s |
| Phase 4 — CI Pipeline | ✅ | `build-and-test` ✅, `eval-gate` ✅, `docker-compose` ✅; `chaos-test` failure on base commit (known pre-existing flakiness) |
| Phase 4 — Runbook | ✅ | `ops/runbooks/day_34_runbook.md` with reindex steps, index corruption recovery, failure modes |

---

## Deliverables Reviewed

| # | Artifact | Verdict | Notes |
|---|---|---|---|
| 1 | `src/FastAPIService/app/api/v1/assistant.py` | ✅ | `POST /v1/assistant/ask` — citation-grounded Q&A endpoint |
| 2 | `src/FastAPIService/app/rag/assistant_index.py` | ✅ | Corpus ingestion, chunker (500-char, 50 overlap), Chroma local vector store persisted to `/var/kendo/assistant_index/` |
| 3 | `src/FastAPIService/app/rag/assistant_chain.py` | ✅ | Citation-grounded RAG chain — source file + line range extraction |
| 4 | `src/FastAPIService/app/main.py` | ✅ | Assistant router registered on startup |
| 5 | `src/Gateway/Controllers/AssistantController.cs` | ✅ | Proxies to FastAPI — non-blocking |
| 6 | `src/Gateway/Services/IFastAPIClient.cs` | ✅ | Added `AssistantAskAsync` contract |
| 7 | `src/Gateway/Services/FastAPIClient.cs` | ✅ | Implemented `AssistantAskAsync` |
| 8 | `scripts/assistant-reindex.sh` | ✅ | Atomic, debounced (5 min) CLI reindex wrapper |
| 9 | `tests/fastapi/test_assistant.py` | ✅ | 388 test lines — citation accuracy, faithfulness, latency, corruption recovery |
| 10 | `tests/Kendo.Tests/Integration/FastAPIWorkloadTests.cs` | ✅ | W6 integration tests (Category=FastAPIW6) |
| 11 | `ops/runbooks/day_34_runbook.md` | ✅ | Reindex verification, corpus scope, failure mode coverage |

---

## Spec Fidelity Check

`day_33_spec.md` was compared against the implementation by reference to `fastapi_rag_service_spec.md §W6`:

| Spec Requirement | Implemented | Status |
|---|---|---|
| `POST /v1/assistant/ask` endpoint | `assistant.py` — FastAPI route | ✅ |
| Corpus ingestion from `docs/architecture/`, `ops/runbooks/`, `templates/skills/`, `changelog/`, `.ai/orchestration.md` | `assistant_index.py` — globs known paths, excludes `.ai/current_state.md`, `.ai/entrypoint.md`, `templates/agents/` | ✅ |
| Chunking (500-char, 50 overlap) | `assistant_index.py` — configurable chunk size + overlap | ✅ |
| Local vector store (Chroma) | `assistant_index.py` — persisted to `/var/kendo/assistant_index/` | ✅ |
| Citation extraction (source + line range) | `assistant_chain.py` — per-chunk citation metadata | ✅ |
| Citation accuracy ≥ 95% | Verified via pytest | ✅ |
| Answer faithfulness ≥ 0.9 | Verified via pytest on held-out QA set | ✅ |
| Latency p95 ≤ 4s | Verified via pytest (local store, no network) | ✅ |
| Index rebuild ≤ 1 per 5 minutes | `scripts/assistant-reindex.sh` — lock file at `/tmp/kendo_assistant_reindex.lock` | ✅ |
| Atomic index rebuild (temp dir → swap) | `assistant_index.py` — build in temp dir, `os.replace()` on success | ✅ |
| 503 on missing/corrupt index | `assistant.py` — RFC 7807 with instruction to run reindex | ✅ |
| Gateway proxy to FastAPI | `AssistantController.cs` + `AssistantAskAsync` | ✅ |

---

## Corpus Scope

Per spec discussion in Day 34 session, the corpus explicitly excludes operational/agent-internal context:

| Included | Excluded |
|---|---|
| `docs/architecture/day_*.md` | `.ai/current_state.md` |
| `ops/runbooks/*.md` | `.ai/entrypoint.md` |
| `templates/skills/*.md` | `templates/agents/` |
| `changelog/*.md` | |
| `.ai/orchestration.md` | |

---

## Regression Assessment

- **No .NET schema changes** — only controller + client method additions
- **No Python route changes** — only new modules added (no existing routes modified)
- **No external network dependency** — local Chroma store means no circuit breaker needed
- **All existing CI checks pass** on the feature branch (chaos-test flakiness is pre-existing)
- **Index rebuild is safe:** atomic swap, lock-file debounced, stale lock auto-detection (1h timeout)
