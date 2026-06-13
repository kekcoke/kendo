# Ops Runbook — Day 1

> **Milestone:** M1.1 — Multi-service scaffold  
> **Services:** Gateway (port 5000), UserService (port 5001), Worker (port 5002)  
> **Date:** 2026-06-13

---

## Health & Observability Contract

| Service | Liveness Probe | Readiness Probe | Expected Response |
|---------|---------------|-----------------|-------------------|
| Gateway | `GET :5000/health/live` | `GET :5000/health/ready` | `200 OK` — `Healthy` / `Ready` |
| UserService | `GET :5001/health/live` | `GET :5001/health/ready` | `200 OK` — `Healthy` / `Ready` |
| Worker | `GET :5002/health/live` | `GET :5002/health/ready` | `200 OK` — `Healthy` / `Ready` |

**Docker health checks:** All three services configured with `interval: 10s`, `timeout: 5s`, `retries: 3`, `start_period: 10s`.

---

## Deployment Playbook

### Prerequisites
- Docker Desktop (or Docker Engine + Compose v2) installed
- .NET 10 SDK installed (for local builds outside Docker)
- Ports 5000, 5001, 5002 available

### Deploy Locally

```bash
# 1. Clone and checkout
git checkout develop
git pull origin develop
git checkout feature/day-01-multi-service-scaffold

# 2. Build images
docker compose build

# 3. Start services
docker compose up -d

# 4. Wait for healthy (all three should show healthy)
docker compose ps

# 5. Verify endpoints
curl http://localhost:5000/health/live
curl http://localhost:5001/health/live
curl http://localhost:5002/health/live
```

### Deploy via CI
Push to `feature/day-01-multi-service-scaffold` or open a PR against `develop`.  
The CI pipeline (`.github/workflows/ci.yml`) runs: build → unit tests → docker compose → health verification.

---

## Rollback Plan

### Local Rollback
```bash
# Stop and remove containers
docker compose down

# Switch back to develop
git checkout develop

# Rebuild if needed from clean state
docker compose build --no-cache
docker compose up -d
```

### CI/Pipeline Rollback
- If the Docker health check step fails: the pipeline exits 1, blocking the PR merge.
- Revert the failing commit on the feature branch and force-push.

---

## Incident Triage

| Symptom | Check | Recovery |
|---------|-------|----------|
| `docker compose ps` shows `unhealthy` | `docker compose logs <service>` | Check port conflicts, restart with `docker compose restart` |
| Health endpoint returns non-200 | `curl -v http://localhost:5000/health/live` | Check ASPNETCORE_URLS, rebuild image |
| Container exits immediately | `docker compose logs --tail=50` | Validate .NET 10 runtime, check for startup exceptions |
| Port conflict | `lsof -i :5000` | Kill conflicting process or change port in `.env` |
