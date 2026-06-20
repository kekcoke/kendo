# Architecture Spec — Day 25

> **Milestone:** M5.6 — Observability + chaos + runbook  
> **Roadmap phase:** 05 — AI/Vector Service (FastAPI)  
> **Status:** Draft — Phase 1 output  
> **Spec basis:** Adopted from `docs/architecture/fastapi_rag_service_spec.md` §Implementation Plan (Units 5–6)  
> **Date:** 2026-06-19

---

## Milestone Scope

- **Milestone:** M5.6 — Observability + chaos + runbook
- **Roadmap phase:** 05 — AI/Vector Service (FastAPI)
- **Components touched:**
  - `FastAPI Rate Limiter` (token-bucket per JWT subject; `429 + Retry-After`)
  - `FastAPI Chaos Suite` (pytest chaos tests: DB down, Azure OpenAI down, FastAPI crash, slow stream)
  - `FastAPI Runbook` (`ops/runbooks/fastapi_service.md` — consolidated failure-mode playbook)
- **Explicitly out of scope:**
  - M5.7 — W1 Event Ingestion RAG (requires M5.6 foundation complete)
  - M5.14 — W8 Evaluation & Regression Gate (requires M5.1–M5.6 complete + at least one workload's dataset)
  - Any new .NET service or Gateway changes (Gateway integration was M5.4, already ✅)
  - `pybreaker` / `tenacity` / OTel tracing (these are M5.5, already ✅ as of Day 24)

---

## Layer Changes

| Layer | Change | Responsible Service |
|---|---|---|
| `FastAPIService` (Python) | **Add** token-bucket rate limiter middleware (per JWT subject) | FastAPI |
| `FastAPIService` (Python) | **Add** chaos test suite (pytest, `Category=Chaos`) | FastAPI |
| `FastAPI` tests | **Create** `tests/resilience/test_rate_limit.py` — rate limit assertions | FastAPI |
| `FastAPI` tests | **Create** `tests/chaos/test_chaos_db_down.py` — DB-down scenario | FastAPI |
| `FastAPI` tests | **Create** `tests/chaos/test_chaos_llm_down.py` — Azure OpenAI down scenario | FastAPI |
| `FastAPI` tests | **Create** `tests/chaos/test_chaos_crash.py` — FastAPI crash + recovery scenario | FastAPI |
| `FastAPI` tests | **Create** `tests/chaos/test_chaos_slow_stream.py` — slow-stream scenario | FastAPI |
| `ops/runbooks/` | **Create** `fastapi_service.md` — failure-mode playbook | — |
| `.github/workflows/ci.yml` | **Add** `chaos-test` job step: `pytest tests/chaos/ --chaos` (tag-gated) | CI |
| `.env.example` | **Add** `FASTAPI__RATE_LIMIT__TOKENS_PER_WINDOW` and related rate-limit env vars | Config |

---

## Data Contracts

### Rate Limiter — Token Bucket

```
Middleware: RateLimitMiddleware
Position: After JWT auth, before route handlers

Configuration (env vars):
  FASTAPI__RATE_LIMIT__TOKENS_PER_WINDOW  int     default=100   # Max requests per window
  FASTAPI__RATE_LIMIT__WINDOW_SECONDS     int     default=60    # Sliding window duration
  FASTAPI__RATE_LIMIT__BUCKET_CAPACITY    int     default=100   # Max burst capacity

Behaviour:
  - Keyed by JWT `sub` claim (user identifier)
  - Missing/invalid JWT → bucket keyed by client IP (fallback)
  - Health endpoints (/health/live, /health/ready) EXEMPT
  - On exceeded: HTTP 429 + Retry-After header (seconds until next token)
  - Response body: RFC 7807 application/problem+json with trace_id
```

### Rate Limit Exceeded Response

```http
HTTP/1.1 429 Too Many Requests
Retry-After: 42
Content-Type: application/problem+json

{
  "type": "https://httpstatuses.io/429",
  "title": "Too Many Requests",
  "status": 429,
  "detail": "Rate limit exceeded. Retry after 42 seconds.",
  "trace_id": "a1b2c3d4e5f6g7h8",
  "rate_limit": {
    "limit": 100,
    "window_seconds": 60,
    "remaining": 0,
    "reset_after_seconds": 42
  }
}
```

### Chaos Test — Environment Variables

```
For chaos test execution (CI only):
  FASTAPI__CHAOS__ENABLED           bool    default=false   # Enable chaos injection
  FASTAPI__CHAOS__DB_DELAY_MS       int     default=0       # Artificial DB latency (ms)
  FASTAPI__CHAOS__DB_FAIL_RATE      float   default=0.0     # % of DB queries to fail (0.0–1.0)
  FASTAPI__CHAOS__LLM_DELAY_MS      int     default=0       # Artificial LLM latency (ms)
  FASTAPI__CHAOS__LLM_FAIL_RATE     float   default=0.0     # % of LLM calls to fail (0.0–1.0)
  FASTAPI__CHAOS__STREAM_DELAY_MS   int     default=0       # Per-token streaming delay (ms)
```

### Runbook Structure — `ops/runbooks/fastapi_service.md`

```markdown
# FastAPI Service — Incident Runbooks

> Consolidated failure-mode playbook for the Python AI/Vector service.  
> All other services retain their per-day runbooks in `ops/runbooks/`.

## Table of Contents
1. [Azure OpenAI Outage](#azure-openai-outage)
2. [pgvector Read Replica Failover](#pgvector-read-replica-failover)
3. [FastAPI Crash Loop](#fastapi-crash-loop)
4. [JWKS Rotation](#jwks-rotation)

## Azure OpenAI Outage
### Symptoms
- FastAPI `/health/ready` shows `llm: unhealthy`
- RAG queries return 503 with `"detail": "LLM service unavailable"`
- Circuit breaker transitions to OPEN state

### Response
1. ...

...
```

---

## Implementation Plan (Commit Units)

### Unit 1 — Token-bucket rate limiter (FastAPI middleware + tests)

- **Files:**
  - `src/FastAPIService/app/middleware/rate_limit.py` — `RateLimitMiddleware` (token-bucket per JWT `sub`, RFC 7807 429 response, Retry-After header)
  - `src/FastAPIService/app/config.py` — add `FASTAPI__RATE_LIMIT__*` settings to `Settings` pydantic model
  - `tests/resilience/test_rate_limit.py` — unit tests: below-limit passes, over-limit 429, Retry-After header present, health endpoints exempt
  - `.env.example` — add rate-limit env vars
- **Gate command:** `cd src/FastAPIService && python -m pytest tests/resilience/test_rate_limit.py -v`
- **Commit message:** `feat(fastapi): add token-bucket rate limiter per JWT subject with 429 + Retry-After`

### Unit 2 — Chaos test suite (pytest chaos utilities + scenario test files)

- **Files:**
  - `tests/chaos/conftest.py` — chaos fixture: docker-compose up, health-wait, teardown
  - `tests/chaos/test_chaos_db_down.py` — simulate pgvector unavailability via docker pause, assert 503 + circuit breaker OPEN
  - `tests/chaos/test_chaos_llm_down.py` — simulate Azure OpenAI unavailability via mock, assert 503 + independent pgvector breaker stays CLOSED
  - `tests/chaos/test_chaos_crash.py` — SIGKILL FastAPI container, assert Gateway falls back to RFC 7807 503, docker-compose restart → health restored within TTL
  - `tests/chaos/test_chaos_slow_stream.py` — simulate per-token delay, assert SSE stream completes within timeout with partial=false, no client hang
  - `tests/fastapi/pytest.ini` or `pyproject.toml` — add markers: `chaos`, `resilience`, `slow`
- **Gate command:** `cd src/FastAPIService && python -m pytest tests/chaos/ -v --chaos`
- **Commit message:** `test(fastapi): add chaos test suite — DB down, LLM down, crash, slow stream`

### Unit 3 — Runbook + CI pipeline integration

- **Files:**
  - `ops/runbooks/fastapi_service.md` — 4-section failure-mode playbook (Azure OpenAI outage, pgvector failover, crash loop, JWKS rotation)
  - `.github/workflows/ci.yml` — add chaos-test job step after python-tests: `pytest tests/chaos/ -v --chaos --junitxml=chaos-report.xml`
- **Gate command:** `git diff --stat` (verify files created) + `pytest tests/chaos/ --collect-only` (test discovery)
- **Commit message:** `ops(fastapi): add fastapi_service.md runbook and chaos-test CI job integration`

---

## Success Checklist

| # | Criterion | Maps to |
|---|-----------|---------|
| 1 | Rate limiter returns `429 + Retry-After` above the configured per-JWT threshold | Roadmap M5.6 (table item 9) |
| 2 | Chaos suite runs in CI: DB down, Azure OpenAI down, FastAPI crash, slow stream — all produce structured pass/fail report | Roadmap M5.6 (table item 10) |
| 3 | Runbook `ops/runbooks/fastapi_service.md` covers: Azure OpenAI outage, pgvector read replica failover, FastAPI crash loop, JWKS rotation | Roadmap M5.6 (table item 11) |
| 4 | All Phase 01–03 acceptance criteria still pass after FastAPI changes (regression guard) | Inherited |
| 5 | pybreaker circuit breakers (pgvector + Azure OpenAI) remain independent — one dependency's failure does not trip the other | M5.5 parity (re-verified) |
| 6 | OpenTelemetry trace IDs appear in every log/response line (correlation enforced) | M5.5 parity (re-verified) |

---

## Resilience Mandate

### Rate Limiter
- **Configuration:** Token-bucket per JWT subject. Missing JWT → client IP fallback.
- **Exhaustion behaviour:** `429 Too Many Requests` with `Retry-After` header and RFC 7807 body.
- **Health exemption:** `/health/live` and `/health/ready` bypass rate limiter entirely.
- **Independent from pybreaker:** Rate limiting is a separate concern from circuit breaking. Rate limiting regulates *client throughput*; circuit breaking protects *downstream dependency health*. Both can fire independently.

### Chaos Suite
- **Test isolation:** Each chaos test runs in its own docker-compose stack (CI-level). Parallel execution is acceptable if resource constraints permit.
- **CI gating:** Chaos tests are gated by `--chaos` marker. CI chaos-test job runs after regular python tests pass. A chaos regression does NOT block the main build-and-test pipeline but IS a blocking condition for Phase 4b review.
- **Flakiness mitigation:** Follow the pattern from M3.6 runbooks: retry loop after `docker compose unpause` before asserting. Known flakiness (connection-refused transient) is documented in `ops/runbooks/fastapi_service.md` §Known CI Flakiness (inherited from M3.6).

### Runbook
- **No new external dependencies.** The runbook documents manual recovery procedures and automated detection pathways. No new automation code is required in this unit.

### Existing M5.5 resilience (re-verified, no changes)
- pybreaker circuit breakers for pgvector and Azure OpenAI (fail_max=3, reset_timeout=30s, independent state)
- tenacity retry with exponential backoff + jitter (DB: all PostgresError; LLM: 5xx only, 4xx excluded)
- OpenTelemetry tracing (OTLP/console exporter, trace_id in RFC 7807, traceparent propagation)

---

## Depends on

| Resource | Required? | Status | Notes |
|---|---|---|---|
| `docs/architecture/fastapi_rag_service_spec.md` | Yes | 🟡 Planned | Pre-authored spec; this day's spec adopts its M5.6 units |
| M5.5 resilience (pybreaker + tenacity + OTel) | Yes | ✅ Day 24 | Already merged, tested, passing |
| M5.4 Gateway routing (IFastAPIClient) | Yes | ✅ Day 23 | Already merged; Gateway proxy routes exist |
| M5.2 LangChain RAG pipeline | Yes | ✅ Day 22 | /v1/rag/query + /v1/rag/stream endpoints exist |
| M5.1 FastAPI scaffold | Yes | ✅ Day 21 | Service containerized, health endpoints, JWT auth |
| M5.3 pgvector read-only role (fastapi_ro) | Yes | ✅ Day 22 | Read-only DSN enforced at DB level |
| M3.6 Runbooks (chaos flakiness pattern) | Reference | ✅ Day 16 | Known CI flakiness in `ops/runbooks/db-failover.md` §Known CI Flakiness |
| Azure OpenAI endpoint + key | Yes | Provided via env vars | FASTAPI__AZURE_OPENAI__* |
| Python 3.12 + dependencies | Yes | Available in `python:3.12-slim` | No new base image changes needed |

---

## Open Questions / Clarifications

1. **Rate limiter scope:** The spec defines rate limiting per JWT `sub` (default 100 req/60s). Should there be a separate, stricter global rate limit (e.g., 500 req/minute across all users) as a safety net? **Proposal:** Ship per-JWT only now; global safety net is a future concern if the Gateway rate limiter (M3.4) is insufficient.
2. **Chaos test CI bottleneck:** The chaos-tests require docker-compose inside CI. If the existing `.NET` chaos-tests already use the full stack, can the FastAPI chaos tests piggyback on the same docker-compose lifecycle? **Proposal:** Yes — the existing `.github/workflows/ci.yml` already starts the full stack. Add a `chaos-test-python` job step after the `.NET` chaos tests pass, reusing the same containers.
3. **Known flakiness pattern adoption:** The M3.6 runbooks document `test_db_downtime` intermittently returning `000000` (connection refused) instead of expected `503`. The M5.6 chaos suite should adopt mitigation #2 (retry loop after `docker compose unpause`). Confirm this is acceptable. **Proposal:** Adopt the mitigation in the test fixture's `docker unpause` teardown logic.
