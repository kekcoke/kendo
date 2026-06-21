# Day 34 Runbook — W6 Document Q&A / Onboarding Assistant (M5.12)

> **Milestone:** M5.12 — W6 Document Q&A / Onboarding Assistant (P2)  
> **Service:** FastAPI + Gateway  
> **Date:** 2026-06-20  
> **Maps to:** `docs/architecture/fastapi_rag_service_spec.md §W6`  
> **Deployment:** No new infrastructure — local Chroma vector store, no external dependencies

---

## 1. Deployment Steps

**No new env vars.** All index paths are hard-coded (local disk only).

**Post-deployment checklist:**
- [ ] Run `scripts/assistant-reindex.sh` to build initial corpus index
- [ ] Verify index exists: `ls -la /var/kendo/assistant_index/`
- [ ] Smoke test the assistant endpoint (see §3)

**Index path:** `/var/kendo/assistant_index/` — persisted Chroma database. Create if missing:
```bash
sudo mkdir -p /var/kendo/assistant_index/
sudo chown $(whoami) /var/kendo/assistant_index/
```

---

## 2. Failure Modes

### 2.1 Index Not Initialized

**Symptom:** `POST /v1/assistant/ask` returns 503 with RFC 7807 body
```json
{
  "title": "Assistant index unavailable",
  "detail": "Run scripts/assistant-reindex.sh",
  "status": 503
}
```
**Remedy:**
```bash
./scripts/assistant-reindex.sh
```

### 2.2 Index Corruption

**Symptom:** Vector search returns empty results or nonsensical citations
**Cause:** Partial write, disk full, or process killed during index build
**Remedy:** Rebuild from scratch:
```bash
sudo rm -rf /var/kendo/assistant_index/
./scripts/assistant-reindex.sh
```

### 2.3 Concurrent Reindex

**Symptom:** `scripts/assistant-reindex.sh` exits with lock error
**Cause:** Another reindex process is already running
**Behavior:** Second process exits immediately (idempotent)
**Remedy:** Wait for first process to complete. Lock auto-expires after 1 hour if process crashes.

### 2.4 Corpus File Missing or Unreadable

**Symptom:** Index build completes but corpus coverage is lower than expected
**Cause:** Files moved, permissions changed, or corpus directory restructured
**Remedy:** Check `assistant_index.py` log for skipped files. Verify corpus scope:
```bash
for dir in docs/architecture ops/runbooks templates/skills changelog .ai; do
  echo "$dir: $(find $dir -name '*.md' | wc -l) files"
done
```

### 2.5 Disk Full

**Symptom:** Index build fails with disk quota exceeded
**Remedy:** Free disk space or reconfigure index path to a volume with more space. Index size scales with corpus — estimate: ~1MB per 100 pages of markdown.

---

## 3. Verification

### Initial index build
```bash
./scripts/assistant-reindex.sh
# Expected: "Corpus reindex complete — 0 chunks processed" (may vary)
```

### FastAPI smoke test
```bash
curl -X POST http://localhost:8000/v1/assistant/ask \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $(cat /tmp/test-jwt)" \
  -d '{"query": "What does the W5 backfill milestone do?"}'

# Expected: 200 OK with answer string and citations array
# Each citation has: source, line_range, text
```

### Gateway smoke test
```bash
curl -X POST http://localhost:5000/api/assistant/ask \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $(cat /tmp/test-jwt)" \
  -d '{"query": "How do I deploy the FastAPI service?"}'

# Expected: 200 OK with cited answer referencing ops/runbooks/
```

### Edge cases
| Test | Expected |
|------|----------|
| Empty query | `400` with RFC 7807 body |
| Off-topic query ("What is the meaning of life?") | Answer may vary but includes citations |
| Index missing | `503` with RFC 7807 body (see §2.1) |
| Query about excluded files (current_state.md) | Answer should not reference excluded sources |
| Gateway proxy | Returns same format as FastAPI directly |

### Integration tests
```bash
# Run FastAPI W6 tests
cd src/FastAPIService
pytest tests/fastapi/test_assistant.py -v

# Run Gateway W6 tests
cd src/Gateway
dotnet test tests/Kendo.Tests --filter "Category=FastAPIW6"
```

---

## 4. Rollback

W6 consists of additive changes only. Rollback by:
1. Revert Gateway commit: `git revert <sha of 1a9c7a0>`
2. Revert FastAPI commits (assistant.py, assistant_index.py, assistant_chain.py)
3. Revert merge: `git revert 23ff73d` on `develop`
4. Delete local index: `rm -rf /var/kendo/assistant_index/`

---

## 5. Dependencies
- `docs/architecture/day_33_spec.md` — spec authority
- `docs/architecture/fastapi_rag_service_spec.md §W6` — data contracts
- **No external dependencies** — local Chroma store, no network calls for retrieval

## 6. Corpus Scope

| Included | Excluded |
|---|---|
| `docs/architecture/day_*.md` | `.ai/current_state.md` |
| `ops/runbooks/*.md` | `.ai/entrypoint.md` |
| `templates/skills/*.md` | `templates/agents/` |
| `changelog/*.md` | |
| `.ai/orchestration.md` | |
