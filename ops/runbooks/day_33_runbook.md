# Day 33 Runbook — W4 User Intent Classification (M5.10)

> **Milestone:** M5.10 — W4 User Intent Classification (P1)  
> **Services:** FastAPI + Gateway  
> **Date:** 2026-06-20  
> **Maps to:** `docs/architecture/fastapi_rag_service_spec.md §W4`  
> **Deployment:** No new infrastructure — reuses existing FastAPI + Gateway

---

## 1. Deployment Steps

**New env vars (FastAPI):**

| Variable | Default | Description |
|---|---|---|
| `FASTAPI__INTENT__MODEL` | `gpt-4o-mini` | LLM model for intent classification |
| `FASTAPI__INTENT__TIMEOUT` | `60` | LLM call timeout in seconds |

**Gateway:** No new env vars. `IntentAdvisoryMiddleware` already has hard-coded fallback.

---

## 2. Failure Modes

### 2.1 FastAPI Classifier Unavailable

**Symptom:** Gateway logs `WARNING: FastAPI intent classify failed, using fallback route`
**Cause:** FastAPI down, timeout, or returns 503
**Behavior:** Gateway uses hard-coded fallback route, request succeeds. No client impact beyond potential sub-optimal routing.
**Remedy:**
1. Check FastAPI health: `curl http://fastapi:8000/health/live`
2. Check FastAPI logs for classifier errors
3. Resolve FastAPI issue — Gateway auto-recovers when FastAPI is healthy

### 2.2 Classifier Timeout (LLM Slow)

**Symptom:** Gateway logs `WARNING: Intent classify timed out, using fallback route`
**Cause:** LLM inference takes > 2s (Gateway timeout for intent hot path)
**Behavior:** Gateway falls back, request succeeds
**Remedy:**
1. Check `FASTAPI__INTENT__TIMEOUT` — model may be overloaded
2. Consider switching to a faster/quantized local model

### 2.3 Low Confidence Classification

**Symptom:** FastAPI returns `{"route": "...", "confidence": 0.45}`
**Cause:** Query doesn't strongly match any available route
**Behavior:** Gateway uses fallback route (advisory only — confidence threshold not enforced at Gateway level)
**Remedy:** Monitor confidence distribution via logs — flag systematic low confidence as a training data issue

### 2.4 Incorrect Classification

**Symptom:** Wrong route suggested by classifier
**Impact:** Gateway still has hard-coded fallback as final decision — incorrect advisory is logged but does not cause incorrect routing
**Remedy:** Improve training examples or switch model

---

## 3. Verification

### FastAPI smoke test
```bash
curl -X POST http://localhost:8000/v1/intent/classify \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $(cat /tmp/test-jwt)" \
  -d '{
    "body": "I want to create a birthday party for my son",
    "user_id": "test-user-123",
    "available_routes": ["events.ingest", "events.validate", "support.create", "assistant.ask"]
  }'

# Expected: 200 OK with {"route": "events.ingest", "confidence": 0.95}
```

### Fallback verification (FastAPI down)
```bash
# Simulate FastAPI being down
curl -X POST http://localhost:5000/api/events \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $(cat /tmp/test-jwt)" \
  -d '{...}'

# Expected: 200 OK (Gateway uses fallback route)
# Check Gateway logs for warning message
```

### Edge cases
| Test | Expected |
|------|----------|
| Empty body | `400` with RFC 7807 (FastAPI) |
| Unknown route in available_routes | Still classified against provided routes |
| FastAPI down | Gateway fallback, logged warning, no 500 to client |
| LLM timeout | FastAPI returns fallback decision, not 500 |

### Integration tests
```bash
# Run FastAPI W4 tests
cd src/FastAPIService
pytest tests/fastapi/test_intent.py -v

# Run Gateway W4 tests
cd src/Gateway
dotnet test tests/Kendo.Tests --filter "Category=FastAPIW4"
```

---

## 4. Rollback

1. Revert FastAPI commit: `git revert <sha of c77cc55>`
2. Revert Gateway changes: revert `IntentAdvisoryMiddleware.cs` to remove FastAPI call
3. Gateway continues routing with hard-coded fallback — same behavior as before W4

---

## 5. Dependencies
- `docs/architecture/day_32_spec.md` — spec authority
- `docs/architecture/fastapi_rag_service_spec.md §W4` — data contracts
- `src/Gateway/Middleware/IntentAdvisoryMiddleware.cs` — existing middleware, modified
