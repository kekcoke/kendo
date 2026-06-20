# Architecture Spec — Day 24: FastAPI Resilience Parity (pybreaker + tenacity)

> **Milestone:** M5.5 — Resilience parity: `pybreaker` circuit breakers + `tenacity` retry
> **Roadmap phase:** 05 — AI/Vector Service (FastAPI)
> **Status:** 🟡 Adopted from `docs/architecture/fastapi_rag_service_spec.md` Unit 4 — generated
> **Spec date:** 2026-06-19

---

## Milestone Scope

- **Milestone:** M5.5 — Resilience parity: `pybreaker` circuit breakers per external dependency (pgvector, Azure OpenAI); `tenacity` retry with exponential backoff + jitter; no raw exceptions cross the network boundary
- **Roadmap phase:** 05 — AI/Vector Service (FastAPI)
- **Components touched:**
  - `src/FastAPIService/app/resilience/circuit_breaker.py` — new
  - `src/FastAPIService/app/resilience/retry.py` — new
  - `src/FastAPIService/app/observability/tracing.py` — new
  - `src/FastAPIService/app/middleware/trace_propagation.py` — new
  - `src/FastAPIService/app/middleware/problem_details.py` — modify; populate `trace_id`
  - `src/FastAPIService/app/db.py` — modify; wrap with circuit breaker + retry
  - `src/FastAPIService/app/rag/chain.py` — modify; wrap LLM calls
  - `src/FastAPIService/app/config.py` — modify; add resilience/otel settings
  - `src/FastAPIService/app/main.py` — modify; init tracing in lifespan
  - `src/FastAPIService/pyproject.toml` — modify; add deps
  - `tests/resilience/test_circuit_breaker.py` — new
  - `tests/resilience/test_retry.py` — new
  - `ops/runbooks/day_24_runbook.md` — new
- **Explicitly out of scope:** M5.6 (chaos suite + runbook consolidation); M5.7+ (workload sub-milestones)

## Layer Changes

| Layer | Change |
|---|---|
| `app/resilience/` | New module: `circuit_breaker.py` + `retry.py` with pybreaker/tenacity |
| `app/observability/` | New module: `tracing.py` — OTel TracerProvider + OTLP exporter |
| `app/middleware/` | New: `trace_propagation.py`; modified: `problem_details.py` — populate `trace_id` |
| `app/db.py` | Wrap `acquire()` with CB+retry; `check_connection()` too |
| `app/rag/chain.py` | Wrap LLM `httpx` call with CB+retry |
| `app/config.py` | Add `breaker_*`, `retry_*`, `otel_*` settings |
| `app/main.py` | Call `init_tracing()` in lifespan startup |
| `pyproject.toml` | Add `pybreaker`, `tenacity`, `opentelemetry-*` |
| `tests/resilience/` | New test directory with unit tests |
| `ops/runbooks/day_24_runbook.md` | New runbook |

## Data Contracts

### Settings additions (`config.py`)

```python
# --- Resilience (pybreaker) ---
breaker_pgvector_fail_max: int = 3
breaker_pgvector_reset_timeout: int = 30
breaker_openai_fail_max: int = 3
breaker_openai_reset_timeout: int = 30

# --- Resilience (tenacity) ---
retry_max_attempts: int = 3
retry_min_wait: float = 1.0
retry_max_wait: float = 30.0
retry_multiplier: float = 2.0

# --- OpenTelemetry ---
otel_service_name: str = "kendo-fastapi"
otel_exporter_otlp_endpoint: str = ""  # empty = console exporter
```

### Circuit Breaker API (`circuit_breaker.py`)

```python
class CircuitBreakerState(enum.Enum):
    CLOSED = "closed"
    OPEN = "open"
    HALF_OPEN = "half_open"

def create_pgvector_breaker(fail_max: int, reset_timeout: int) -> pybreaker.CircuitBreaker
def create_openai_breaker(fail_max: int, reset_timeout: int) -> pybreaker.CircuitBreaker
```

- Each breaker is a singleton instance registered in `app.state.resilience` on startup
- Metrics: `circuit_breaker_state{name="pgvector"}` gauge exposed for chaos test assertions

### Retry Config (`retry.py`)

```python
# Decorator for async DB operations
@tenacity.retry(
    stop=stop_after_attempt(3),
    wait=wait_exponential_jitter(initial=1.0, max=30.0, jitter=0.5),
    retry=retry_if_exception_type((asyncpg.exceptions.PostgresError, ...)),
    before_sleep=before_sleep_log(logger, logging.WARNING),
)

# Decorator for async LLM HTTP calls
@tenacity.retry(
    stop=stop_after_attempt(3),
    wait=wait_exponential_jitter(initial=1.0, max=30.0, jitter=0.5),
    retry=retry_if_exception_type((httpx.TimeoutException, httpx.ConnectError, httpx.HTTPStatusError)),
    before_sleep=before_sleep_log(logger, logging.WARNING),
)
```

### OpenTelemetry Trace Attributes

| Span attribute | Value |
|---|---|
| `service.name` | `kendo-fastapi` |
| `kendo.breaker.name` | `pgvector` or `azure_openai` |
| `kendo.breaker.state` | `closed` / `open` / `half_open` |
| `kendo.retry.attempt` | integer |
| `trace_id` | 16-char hex — populated in all RFC 7807 `trace_id` fields |

## Implementation Plan (Commit Units)

### Unit 1 — Dependencies + Settings + Resilience Module
- **Files:**
  - `src/FastAPIService/pyproject.toml` — add `pybreaker>=2.0.0`, `tenacity>=9.0.0`, `opentelemetry-api>=1.30.0`, `opentelemetry-sdk>=1.30.0`, `opentelemetry-exporter-otlp>=1.30.0`, `opentelemetry-instrumentation-fastapi>=0.51b0`, `opentelemetry-instrumentation-asyncpg>=0.51b0`, `opentelemetry-instrumentation-httpx>=0.51b0`
  - `src/FastAPIService/app/resilience/__init__.py` — empty
  - `src/FastAPIService/app/resilience/circuit_breaker.py` — `create_pgvector_breaker()`, `create_openai_breaker()`, `CircuitBreakerState` enum
  - `src/FastAPIService/app/resilience/retry.py` — `retry_db()` decorator factory, `retry_llm()` decorator factory
  - `src/FastAPIService/app/observability/__init__.py` — empty
  - `src/FastAPIService/app/observability/tracing.py` — `init_tracing(settings)` function, TracerProvider, OTLP exporter, `get_tracer()`
  - `src/FastAPIService/app/middleware/trace_propagation.py` — `TracePropagationMiddleware` class, extracts `traceparent` from request, creates child span
  - `src/FastAPIService/app/config.py` — append resilience + otel settings fields
- **Gate command:** `cd src/FastAPIService && python -c "import pybreaker; import tenacity; import opentelemetry; print('deps OK')"`
- **Commit message:** `feat(fastapi): add pybreaker, tenacity, OpenTelemetry deps and resilience module scaffold`

### Unit 2 — Wire resilience into db.py + chain.py + problem_details.py
- **Files:**
  - `src/FastAPIService/app/db.py` — import `create_pgvector_breaker` + `retry_db`; wrap `create_pool()`, `get_pool()`, `check_connection()`, `acquire()` usage with CB+retry
  - `src/FastAPIService/app/rag/chain.py` — import `create_openai_breaker` + `retry_llm`; wrap LLM `httpx` call with CB check + retry
  - `src/FastAPIService/app/middleware/problem_details.py` — import `get_tracer`; populate `trace_id` from `Span.get_current_span().get_span_context().trace_id`
  - `src/FastAPIService/app/main.py` — in lifespan startup: call `init_tracing(settings)`, store breakers in `app.state.resilience`, add `TracePropagationMiddleware`
- **Gate command:** `cd src/FastAPIService && python -c "from app.resilience.circuit_breaker import create_pgvector_breaker; from app.resilience.retry import retry_db; from app.observability.tracing import init_tracing; print('imports OK')"`
- **Commit message:** `feat(fastapi): wire pybreaker+tenacity into db, chain, problem_details, and main.py`

### Unit 3 — Unit tests for circuit breaker + retry
- **Files:**
  - `tests/resilience/__init__.py` — empty
  - `tests/resilience/test_circuit_breaker.py` — unit tests: breaker opens after N failures, resets after timeout, state transitions, independent breakers for pgvector vs OpenAI
  - `tests/resilience/test_retry.py` — unit tests: retry fires on transient errors, max attempts exhausted, exponential backoff progression, logs on retry
- **Gate command:** `cd src/FastAPIService && python -m pytest tests/resilience/ -v`
- **Commit message:** `test(fastapi): add unit tests for pybreaker circuit breaker and tenacity retry`

### Unit 4 — Runbook
- **Files:**
  - `ops/runbooks/day_24_runbook.md` — resilience M5.5 verification, rollback, failure scenarios
- **Gate command:** `cat ops/runbooks/day_24_runbook.md`
- **Commit message:** `docs(fastapi): add Day 24 resilience runbook`

## Success Checklist

| # | Criterion | Maps to |
|---|-----------|---------|
| 1 | `pybreaker.CircuitBreaker` registered for pgvector (independent from Azure OpenAI) | Roadmap M5.5 |
| 2 | `pybreaker.CircuitBreaker` registered for Azure OpenAI (independent from pgvector) | Roadmap M5.5 |
| 3 | Circuit breaker trips after N consecutive failures; subsequent requests return RFC 7807 503 (not raw exception) | Roadmap M5.5 |
| 4 | pgvector breaker and OpenAI breaker trip independently — one dep failure does not trip the other | Roadmap M5.5 |
| 5 | `tenacity` retry with exponential backoff + jitter wraps transient DB failures | Roadmap M5.5 |
| 6 | `tenacity` retry wraps transient LLM HTTP failures | Roadmap M5.5 |
| 7 | Retries visible as span events in OpenTelemetry traces | Roadmap M5.5 |
| 8 | OpenTelemetry trace IDs appear in every log line for a given request (correlation enforced) | Roadmap M5.5 |
| 9 | RFC 7807 `trace_id` field populated with current OTel trace ID | Roadmap M5.5 |
| 10 | `traceparent` header extracted from Gateway request and propagated to child spans | Roadmap M5.5 |
| 11 | Gateway Polly circuit breaker (existing) still trips when FastAPI is down; clients see RFC 7807 503 | Roadmap M5.5 (no change — regression check) |

## Resilience Mandate

### Circuit breaker per external dependency
- **pgvector breaker:** `fail_max=3`, `reset_timeout=30s`. Trips when `asyncpg` pool acquires or queries fail consecutively 3 times.
- **Azure OpenAI breaker:** `fail_max=3`, `reset_timeout=30s`. Trips when LLM HTTP calls (`httpx`) fail (timeout, connection refused, 5xx) consecutively 3 times.
- **State metrics:** Both breakers expose state via `circuit_breaker_state{name="pgvector" or "azure_openai"}` gauge — either directly via OTel metric or logged at state transition for chaos test assertions.

### Retry with exponential backoff + jitter
- **DB retry:** `max_attempts=3`, `min_wait=1s`, `max_wait=30s`, `multiplier=2.0`, jitter=±0.5s. Retries on `asyncpg.exceptions.PostgresError`, `asyncpg.exceptions.ConnectionDoesNotExistError`, `asyncpg.exceptions.CannotConnectNowError`.
- **LLM retry:** Same config. Retries on `httpx.TimeoutException`, `httpx.ConnectError`, `httpx.HTTPStatusError` (5xx only). Does NOT retry on 4xx.

### OpenTelemetry
- **Exporter:** OTLP exporter to `OTEL_EXPORTER_OTLP_ENDPOINT` if set, console exporter fallback.
- **Instrumentation:** `opentelemetry-instrumentation-fastapi` for route spans, `opentelemetry-instrumentation-asyncpg` for DB spans, `opentelemetry-instrumentation-httpx` for outbound HTTP spans.
- **Traceparent propagation:** `TracePropagationMiddleware` extracts `traceparent` header from incoming Gateway requests and sets it as parent for the current span. SSE streaming events include `traceparent` in event metadata.
- **W3C Trace Context:** Trace IDs formatted as 32-hex-char `trace_id` in spans; 16-hex-char `trace_id` truncated for RFC 7807 responses.

### No raw exceptions
Every unhandled exception in a route handler is caught by the RFC 7807 handler (already exists) and emitted as `application/problem+json` with `trace_id` populated (this spec).

### N/A
- No Rebus messaging in FastAPI — async resilience via `asyncio` patterns only
- No Gateway-side Polly changes — Gateway already has its own resilience pipeline

## Depends on

| Dependency | File | Milestone |
|---|---|---|
| Existing FastAPI scaffold | `docs/architecture/day_21_spec.md` | M5.1 |
| Existing pgvector read integration | `docs/architecture/day_22_spec.md` (adopted from `fastapi_rag_service_spec.md`) | M5.2+M5.3 |
| Existing Gateway integration | `docs/architecture/day_23_spec.md` | M5.4 |
| Resilience data contracts | `docs/architecture/fastapi_rag_service_spec.md` § Unit 4, § Resilience Mandate | M5.1–M5.6 |
