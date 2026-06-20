# Architecture Spec — Day 33 — W6: Document Q&A / Onboarding Assistant

> **Milestone:** M5.12 — W6 Document Q&A / Onboarding Assistant (P2)  
> **Roadmap phase:** 05 — AI/Vector Service (FastAPI)  
> **Date:** 2026-06-20  
> **Type:** Workload spec (adopts by reference `fastapi_rag_service_spec.md §W6`)  
> **Depends on:** `docs/architecture/fastapi_rag_service_spec.md` §W6 (design-spec contract, rev 2026-06-16)

---

## Milestone Scope

- **What:** Implement W6 — an internal document Q&A tool. `POST /api/assistant/ask` on the Gateway proxies to FastAPI `/v1/assistant/ask`. FastAPI ingests the project corpus (`docs/architecture/`, `ops/runbooks/`, `templates/skills/`, `changelog/`, `.ai/orchestration.md`) into a local vector index. Queries return cited answers (source file + line range).
- **Maps to:** `fastapi_rag_service_spec.md §W6 — Document Q&A / Onboarding Assistant` (data contracts, acceptance gates)
- **Explicitly out of scope:**
  - W7 (handled in Day 34)
  - External/public access — internal tool only
  - Real-time file watcher (deferred to post-MVP; first version uses CLI-triggered reindex)

---

## Layer Changes

| Layer | Service | Change |
|-------|---------|--------|
| Application | `src/FastAPIService/app/api/v1/assistant.py` | New — `POST /v1/assistant/ask` endpoint |
| Application | `src/FastAPIService/app/rag/assistant_index.py` | New — corpus ingestion + local vector store (Chroma or FAISS) |
| Application | `src/FastAPIService/app/rag/assistant_chain.py` | New — citation-grounded RAG chain |
| Application | `src/Gateway/Controllers/AssistantController.cs` | Modify — proxy to FastAPI instead of stub |
| Application | `src/Gateway/Services/FastAPIClient.cs` | Modify — add `AssistantAskAsync` |

---

## Data Contracts

Adopted by reference from `fastapi_rag_service_spec.md §W6 — Document Q&A / Onboarding Assistant` (rev 2026-06-16). Key additions for this spec:

**Corpus scope** (pinned per spec discussion):
- `docs/architecture/day_*.md` — all architecture specs
- `ops/runbooks/*.md` — all runbooks
- `templates/skills/*.md` — agent skill templates
- `changelog/*.md` — changelog entries
- `.ai/orchestration.md` — orchestration rules
- **Excluded:** `.ai/current_state.md`, `.ai/entrypoint.md`, `templates/agents/` (operational/agent-internal context)

**Gateway → FastAPI:**
```json
POST /v1/assistant/ask
{
  "query": "string"
}
```

**FastAPI → Gateway response:**
```json
{
  "answer": "string",
  "citations": [
    {"source": "docs/architecture/day_22_spec.md", "line_range": "142-148", "text": "snippet"}
  ],
  "trace_id": "string"
}
```

---

## Implementation Plan (Commit Units)

### Unit 1 — FastAPI corpus index + citation-graded RAG chain

**Files:**
- `src/FastAPIService/app/api/v1/assistant.py` — `POST /v1/assistant/ask` route
- `src/FastAPIService/app/rag/assistant_index.py` — corpus scanner (globs known paths), chunker (500-char overlap 50), local vector store (Chroma persisted to `/var/kendo/assistant_index/`)
- `src/FastAPIService/app/rag/assistant_chain.py` — LangChain RAG chain with citation extraction (source + line range)
- `tests/fastapi/test_assistant.py` — citation accuracy + faithfulness tests
- `scripts/assistant-reindex.sh` — CLI wrapper: `./scripts/assistant-reindex.sh` rebuilds index from scratch

**Gate command:** `pytest tests/fastapi/test_assistant.py` — must exit 0 with citation accuracy ≥ 95% and faithfulness ≥ 0.9.

**Commit message:**
```
feat(fastapi): add W6 internal doc Q&A — citation-grounded RAG over project corpus

POST /v1/assistant/ask answers questions from docs/architecture/, ops/runbooks/,
templates/skills/, changelog/, and .ai/orchestration.md. Each answer includes
source file + line range citations. Chroma local vector store persisted to disk.
Implements fastapi_rag_service_spec.md §W6.

Day 33 — M5.12 Unit 1 of 2 | Milestone: M5.12 — W6 Document Q&A / Onboarding Assistant
Coverage: citation accuracy ≥ 95%, faithfulness ≥ 0.9
Lint: clean
```

### Unit 2 — Gateway proxy route

**Files:**
- `src/Gateway/Controllers/AssistantController.cs` — modify to proxy to FastAPI
- `src/Gateway/Services/IFastAPIClient.cs` — add `AssistantAskAsync`
- `src/Gateway/Services/FastAPIClient.cs` — implement `AssistantAskAsync`
- `tests/Kendo.Tests/Integration/FastAPIWorkloadTests.cs` — add W6 tests

**Gate command:** `dotnet test tests/Kendo.Tests --filter "Category=FastAPIW6"` — must exit 0.

**Commit message:**
```
feat(gateway): proxy W6 assistant/ask to FastAPI

Gateway AssistantController forwards POST /api/assistant/ask to FastAPI
POST /v1/assistant/ask via IFastAPIClient.AssistantAskAsync. Returns cited
answers to caller.

Day 33 — M5.12 Unit 2 of 2 | Milestone: M5.12 — W6 Document Q&A / Onboarding Assistant
Coverage: 100% new integration tests
Lint: clean
```

---

## Success Checklist

Maps 1:1 to `fastapi_rag_service_spec.md §W6 acceptance gate`:

| # | Criterion | Maps to |
|---|-----------|---------|
| 1 | Citation accuracy ≥ 95% (cited file + line range actually contains the answer) | M5.12 acceptance |
| 2 | Answer faithfulness ≥ 0.9 on a held-out QA set | M5.12 acceptance |
| 3 | Latency p95 ≤ 4s (local Chroma store — no network dependency) | M5.12 acceptance |
| 4 | Index rebuild ≤ 1 per 5 minutes (debounced CLI; file-watcher deferred) | M5.12 acceptance |
| 5 | `scripts/assistant-reindex.sh` exits 0 and index is queryable after rebuild | Operational safety |

---

## Resilience Mandate

- Local vector store (Chroma) has no external network dependency — no circuit breaker needed
- Index rebuild is single-threaded and atomic: build new index in temp dir, swap atomically on success
- If index is missing or corrupt: FastAPI returns RFC 7807 503 with `title: "Assistant index unavailable"` and `detail: "Run scripts/assistant-reindex.sh"`

---

## Depends on

- `docs/architecture/fastapi_rag_service_spec.md §W6` — data contracts, acceptance gates
- M5.1 (Day 21) — FastAPI scaffold
