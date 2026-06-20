# Day 21 Runbook — FastAPI Service Scaffold

> **Milestone:** M5.1 — Service scaffold: Python 3.12 + FastAPI + Uvicorn containerized  
> **Date:** 2026-06-19  
> **Branch:** `feature/day-21-fastapi-scaffold`  

---

## Component Overview

| Component | Purpose |
|-----------|---------|
| `src/FastAPIService/` | New Python 3.12 FastAPI service directory — scaffold only; no DB or LLM calls yet |
| `pyproject.toml` | Python project manifest with FastAPI, Uvicorn, Pydantic-Settings dependencies |
| `app/main.py` | FastAPI app factory (`create_app()`); lifespan hooks; middleware registration |
| `app/config.py` | Pydantic-Settings config loaded from `FASTAPI__*` env vars |
| `app/routes/health.py` | `/health/live` (always 200) and `/health/ready` (always 200 — placeholder for M5.3+) |
| `app/middleware/auth.py` | JWT auth middleware (RS256, health endpoint exemption); scaffold mode accepts any Bearer token |
| `app/middleware/problem_details.py` | RFC 7807 exception handler — returns `application/problem+json` |
| `Dockerfile` | Multi-stage `python:3.12-slim` build, non-root `fastapi` user, HEALTHCHECK |
| `.dockerignore` | Excludes `__pycache__`, `.venv`, `.git`, `tests/` from build context |

---

## Verification

```bash
# Start the service
docker compose up -d fastapi

# Health endpoints (must return 200)
curl -f http://fastapi:8000/health/live     # {"status":"healthy"}
curl -f http://fastapi:8000/health/ready    # {"status":"ready"}

# JWT enforcement (health endpoints exempt)
curl -w "\n%{http_code}" http://fastapi:8000/v1/nonexistent              # 401
curl -w "\n%{http_code}" -H "Authorization: Bearer t" http://fastapi:8000/v1/nonexistent # 404
```

## Rollback

```bash
docker compose down fastapi
docker rmi kendo-fastapi
```
