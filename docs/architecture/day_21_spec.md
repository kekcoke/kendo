# Architecture Spec — Day 21: FastAPI Service Scaffold

> **Milestone:** M5.1 — Service scaffold
> **Roadmap phase:** 05 — AI/Vector Service (FastAPI)
> **Status:** 🟡 Generated — awaiting Phase 2 implementation
> **Adopts:** `docs/architecture/fastapi_rag_service_spec.md` (pre-authored spec)
> **Spec date:** 2026-06-19

---

## Milestone Scope

- **Milestone:** M5.1 — Service scaffold: Python 3.12 + FastAPI + Uvicorn containerized
- **Roadmap phase:** 05 — AI/Vector Service (FastAPI)
- **Components touched:**
  - `src/FastAPIService/` — new Python 3.12 + FastAPI service project
  - `src/FastAPIService/Dockerfile` — multi-stage build, non-root user
  - `docker-compose.yml` — add `fastapi` service, internal port `8000`, health check
  - `.env.example` — new FastAPI environment variables
- **Explicitly out of scope:**
  - M5.3 (pgvector read-only role) — deferred to future day
  - M5.2 (LangChain RAG pipeline) — deferred to future day
  - M5.4 (Gateway integration) — deferred to future day
  - M5.5 (Resilience parity with pybreaker/tenacity) — deferred to future day
  - M5.6 (Observability + chaos + runbook) — deferred to future day
  - All M5.7+ workload sub-milestones (W1–W8)
  - Any Phase 01–04 work (already complete)

---

## Layer Changes

| Layer | Service | Change |
|-------|---------|--------|
| Application | `FastAPIService` (new) | Python 3.12, FastAPI, Uvicorn, Pydantic v2, Pydantic-Settings |
| Application | `FastAPIService` | Health endpoints (`/health/live`, `/health/ready`) — readiness returns 503 when not ready |
| Application | `FastAPIService` | JWT validation middleware (RS256, JWKS from env var) — skeleton, full wire-up deferred to Unit 2+ |
| Application | `FastAPIService` | RFC 7807 exception handler — skeleton, full wire-up deferred to Unit 2+ |
| Infrastructure | Docker Compose | Add `fastapi` service on internal network, port `8000` internal only, health check |
| Configuration | `.env.example` | New vars: `FASTAPI__JWT__JWKS_URL`, `FASTAPI__ENV`, `FASTAPI__SERVICE_NAME` |

---

## Data Contracts

### `GET /health/live` — Liveness probe
Returns `200 OK` with `{"status": "healthy"}`. No dependency checks.

### `GET /health/ready` — Readiness probe
Returns `200 OK` with `{"status": "ready"}` when all dependencies are reachable.
Returns `503` with RFC 7807 body when any dependency is unhealthy.

Both endpoints are exempt from JWT auth and rate limiting (matching Phase 01 convention).

---

## Implementation Plan (Commit Units)

### Unit 1 — Scaffold FastAPIService project
- **Files:**
  - `src/FastAPIService/pyproject.toml` — `fastapi`, `uvicorn[standard]`, `pydantic`, `pydantic-settings`, `pydantic-settings[dotenv]`
  - `src/FastAPIService/app/__init__.py` — empty package marker
  - `src/FastAPIService/app/main.py` — `FastAPI()` app factory, health endpoints
  - `src/FastAPIService/app/config.py` — `Settings` via `pydantic-settings`
- **Gate command:** `cd src/FastAPIService && uv sync && python -c "from app.main import app; print('ok')"`
- **Commit message:** `feat(fastapi): scaffold FastAPIService project with pydantic-settings`

### Unit 2 — Health endpoints + Dockerfile
- **Files:**
  - `src/FastAPIService/app/routes/__init__.py` — empty package marker
  - `src/FastAPIService/app/routes/health.py` — `/health/live` and `/health/ready` routes
  - `src/FastAPIService/Dockerfile` — multi-stage build, `python:3.12-slim`, non-root `fastapi` user, `HEALTHCHECK` instructions
  - `src/FastAPIService/.dockerignore` — exclude `__pycache__`, `.venv`, `.git`, `tests/`
- **Gate command:** check Dockerfile syntax and health endpoint structure
- **Commit message:** `feat(fastapi): add health endpoints and multi-stage Dockerfile`

### Unit 3 — JWT auth middleware skeleton + RFC 7807 exception handler skeleton
- **Files:**
  - `src/FastAPIService/app/middleware/__init__.py` — empty package marker
  - `src/FastAPIService/app/middleware/auth.py` — JWT validation middleware (RS256, JWKS from `FASTAPI__JWT__JWKS_URL`), passes `request.state.user` to routes; health endpoints exempt
  - `src/FastAPIService/app/middleware/problem_details.py` — RFC 7807 `application/problem+json` exception handler, maps common exceptions
- **Gate command:** verify middleware registration and health endpoint exemptions
- **Commit message:** `feat(fastapi): add JWT auth middleware and RFC 7807 exception handler`

### Unit 4 — docker-compose integration + .env.example
- **Files:**
  - `docker-compose.yml` — add `fastapi` service:
    - `build: ./src/FastAPIService`
    - `container_name: kendo-fastapi`
    - `expose: ["8000"]` (internal network only, not `ports:`)
    - `depends_on: postgres` (expects pgvector at M5.3, placeholder health check for now)
    - `healthcheck: test curl -f http://localhost:8000/health/live`
    - `env_file: .env`
    - `networks: [kendo-network]`
  - `.env.example` — `FASTAPI__JWT__JWKS_URL`, `FASTAPI__ENV=development`, `FASTAPI__SERVICE_NAME=kendo-fastapi`
- **Gate command:** `docker compose config` validates; `docker compose build fastapi` succeeds; `docker compose up -d fastapi` starts; `curl -fsS localhost:8000/health/live` returns 200
- **Commit message:** `chore(docker): integrate FastAPIService into docker-compose with health check`

---

## Success Checklist

| # | Criterion | Maps to |
|---|-----------|---------|
| 1 | `src/FastAPIService/` exists with `pyproject.toml` and `app/main.py` | M5.1 scaffold |
| 2 | `uv sync` resolves all dependencies without error | M5.1 |
| 3 | `src/FastAPIService/Dockerfile` builds successfully (multi-stage, non-root user) | M5.1 |
| 4 | `docker compose up -d fastapi` starts the container; `/health/live` returns `200` | Roadmap M5.1 |
| 5 | `FASTAPI__JWT__JWKS_URL` is configurable via `.env` / env var | M5.1 |
| 6 | Health endpoints (`/health/live`, `/health/ready`) return correct content type | M5.1 |
| 7 | JWT auth middleware rejects requests without `Authorization: Bearer` header (401) | Security baseline |
| 8 | RFC 7807 handler returns `application/problem+json` on unhandled exceptions | M5.1 |
| 9 | `docker compose config` validates the new `fastapi` service definition | M5.1 |
| 10 | All Phase 01–04 tests still pass (regression guard) | Inherited |

---

## Resilience Mandate

- **N/A — no external dependencies this milestone.** M5.1 is scaffolding only. No database, queue, or LLM calls are made. The JWT middleware is initialized but validates locally (no external JWKS fetch until a later unit).
- Health endpoints intentionally bypass JWT and rate limiting — matching the existing Phase 01–04 convention across all platform services.
- The readiness check is a placeholder that always returns ready at this stage; it will be wired to real dependency checks in M5.3+.

---

## Dependencies

| Resource | Required? | Status |
|----------|-----------|--------|
| Python 3.12 | Yes | Available in `python:3.12-slim` Docker base image |
| FastAPI | Yes | `pyproject.toml` dependency in Unit 1 |
| Uvicorn | Yes | `pyproject.toml` dependency in Unit 1 |
| Pydantic v2 | Yes | Included via FastAPI dependency |
| `uv` package manager | Yes | Recommended for speed; `pip` fallback acceptable |
| Docker Compose | Yes | Already exists in repo |
| Gateway (existing) | No | Not called in this milestone |
| UserService (existing) | No | Not called in this milestone |
| PostgreSQL | No (placeholder `depends_on` only) | Not read/written in this milestone |
