# Day 24 Runbook — FastAPI Resilience Parity (M5.5)

> **Milestone:** M5.5 — `pybreaker` circuit breakers + `tenacity` retry + OpenTelemetry tracing
> **Date:** 2026-06-19
> **Feature branch:** `feature/day-24-pybreaker-tenacity-resilience`

---

## Overview

This runbook covers the resilience parity milestone for the FastAPI service:
- Two independent `pybreaker` circuit breakers (pgvector, Azure OpenAI)
- `tenacity` retry with exponential backoff + jitter for DB and LLM calls
- OpenTelemetry TracerProvider with OTLP exporter
- `traceparent` propagation from Gateway requests
- RFC 7807 `trace_id` field populated from OpenTelemetry

## Verification

### 1. Dependencies installed

```bash
cd src/FastAPIService
python -c "import pybreaker; import tenacity; import opentelemetry; print('deps OK')"
```

### 2. Circuit breaker health

```bash
cd src/FastAPIService
python -c "
from app.resilience.circuit_breaker import create_pgvector_breaker, create_openai_breaker
pgv = create_pgvector_breaker(3, 30)
oai = create_openai_breaker(3, 30)
assert pgv.state != oai.state  # independent instances
print('Breakers OK')
"
```

### 3. Retry decorators loadable

```bash
cd src/FastAPIService
python -c "
from app.resilience.retry import retry_db, retry_llm
print('Retry decorators OK')
"
```

### 4. OpenTelemetry initialization

```bash
cd src/FastAPIService
python -c "
from app.observability.tracing import init_tracing, get_tracer
from app.config import Settings
settings = Settings()
init_tracing(settings)
tracer = get_tracer()
assert tracer is not None
print('Tracing OK')
"
```

### 5. Run unit tests

```bash
cd src/FastAPIService
python -m pytest tests/resilience/ -v
```

### 6. Integration gate

```bash
cd src/FastAPIService
python -c "
from app.resilience.circuit_breaker import create_pgvector_breaker, create_openai_breaker
from app.resilience.retry import retry_db, retry_llm
from app.observability.tracing import init_tracing
from app.middleware.trace_propagation import TracePropagationMiddleware
print('All imports OK')
"
```

## Failure Scenarios

### Scenario A: Circuit breaker open — no dependency recovery

**Symptom:** `GET /v1/rag/query` returns `503 Service Unavailable` with RFC 7807 `trace_id`.

**Expected response:**
```json
{
  "type": "about:blank",
  "title": "Service Unavailable",
  "status": 503,
  "detail": "pgvector circuit breaker is open. Dependency unavailable.",
  "trace_id": "a1b2c3d4e5f67890"
}
```

**Steps:**
1. Check breaker state: `python -c "from app.resilience.circuit_breaker import get_breaker_state; print(get_breaker_state('pgvector'))"`
2. Check the failing dependency (pgvector connectivity)
3. Fix the dependency (restart postgres, check network)
4. Breaker auto-resets after `reset_timeout` (30s by default)
5. Retry the request

### Scenario B: Retry exhaustion on transient failure

**Symptom:** Query eventually fails after 3 retries with exponential backoff. Logs show `WARNING` retry events.

**Log pattern:**
```
WARNING  app.resilience.retry:retry.py:42 - Retry attempt 1/3 failed: Connection refused. Waiting 2.0s...
WARNING  app.resilience.retry:retry.py:42 - Retry attempt 2/3 failed: Connection refused. Waiting 4.0s...
WARNING  app.resilience.retry:retry.py:42 - Retry attempt 3/3 failed: Connection refused. Waiting 8.0s...
ERROR    app.resilience.retry:retry.py:45 - All 3 retry attempts exhausted.
```

**Steps:**
1. Check if the dependency is permanently down (breaker should also trip)
2. If permanent: dependency outage — escalate to SRE
3. If transient: verify recovery on next request
4. Check OTel spans for `kendo.retry.attempt` attribute

### Scenario C: trace_id missing in RFC 7807 response

**Symptom:** `trace_id` field is empty string `""` in error responses.

**Root cause:** OpenTelemetry not initialized before first request, or `TracePropagationMiddleware` not registered.

**Steps:**
1. Verify `init_tracing(settings)` is called in lifespan startup
2. Verify `TracePropagationMiddleware` is added in `create_app()`
3. Verify `opentelemetry-instrumentation-fastapi` is installed

## Rollback

### If resilience changes break the service:

1. Identify the breaking commit:
```bash
git log --oneline -5
```

2. Revert:
```bash
git revert <commit-hash>
```

3. Push to feature branch and re-run CI:
```bash
git push origin feature/day-24-pybreaker-tenacity-resilience
```

### If pybreaker/tenacity deps cause import errors:

1. Comment out the imports in `app/main.py`, `app/db.py`, `app/rag/chain.py`
2. Comment out the `pyproject.toml` additions
3. Re-run: `docker compose up -d fastapi`

## Commits

| Unit | Commit | Message |
|---|---|---|
| 1 | Unit 1 | `feat(fastapi): add pybreaker, tenacity, OpenTelemetry deps and resilience module scaffold` |
| 2 | Unit 2 | `feat(fastapi): wire pybreaker+tenacity into db, chain, problem_details, and main.py` |
| 3 | Unit 3 | `test(fastapi): add unit tests for pybreaker circuit breaker and tenacity retry` |
| 4 | Unit 4 | `docs(fastapi): add Day 24 resilience runbook` |
