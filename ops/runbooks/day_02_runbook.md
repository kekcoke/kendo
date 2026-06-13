# Ops Runbook — Day 2

> **Milestone:** M1.2 — Database layer (EF Core + pgvector)  
> **Services:** Gateway (5000), UserService (5001), Worker (5002), PostgreSQL (5432)  
> **Date:** 2026-06-13

---

## Health & Observability Contract

| Service | Liveness Probe | Readiness Probe | Expected Response |
|---------|---------------|-----------------|-------------------|
| Gateway | `GET :5000/health/live` | `GET :5000/health/ready` | `200 OK` — `Healthy` / `Ready` |
| UserService | `GET :5001/health/live` | `GET :5001/health/ready` | `200 OK` — `Healthy` / `Ready` |
| Worker | `GET :5002/health/live` | `GET :5002/health/ready` | `200 OK` — `Healthy` / `Ready` |
| PostgreSQL | `pg_isready -U kendo -d kendo_users` | Same as liveness | Accepting connections |

**Docker health checks:** All four services, `interval: 5-10s`, `retries: 3-5`, `start_period: 10s`.

---

## Database Contract

| Property | Value |
|----------|-------|
| Host | `postgres` (in Docker), `localhost` (dev) |
| Port | `5432` |
| Database | `kendo_users` |
| User | `kendo` |
| Password | `kendo_dev` (dev only — rotate for production) |
| Connection String Env Var | `ConnectionStrings__DefaultConnection` |
| Extension | `vector` (pgvector) — enabled in migration |

---

## Deployment Playbook

### Prerequisites
- Docker Desktop (or Docker Engine + Compose v2) installed
- .NET 10 SDK installed (for local builds)
- Ports 5000, 5001, 5002, 5432 available
- Local PostgreSQL stopped if using port 5432 (`brew services stop postgresql@18`)

### Deploy Locally

```bash
# 1. Checkout feature branch
git checkout feature/day-02-database-layer

# 2. Build images
docker compose build

# 3. Start all services (postgres starts first due to depends_on)
docker compose up -d

# 4. Wait for healthy (all four should show healthy)
docker compose ps

# 5. Verify PostgreSQL
docker compose exec postgres pg_isready -U kendo

# 6. Verify migrations applied
docker compose exec postgres psql -U kendo -d kendo_users -c "SELECT extname FROM pg_extension;"

# 7. Verify health endpoints
curl http://localhost:5000/health/live
curl http://localhost:5001/health/live
curl http://localhost:5002/health/live
```

### Deploy via CI
Push to `feature/day-02-database-layer` or open PR against `develop`.
CI pipeline runs: build → unit tests → data integration tests → docker compose → health verification.

---

## Rollback Plan

### Local Rollback
```bash
# Stop and remove containers, keep data volume
docker compose down

# To also remove database volume (data loss):
docker compose down -v

# Switch to develop
git checkout develop

# Rebuild and start
docker compose build --no-cache
docker compose up -d
```

### Migration Rollback
```bash
# Remove the last migration
dotnet ef migrations remove --project src/UserService --startup-project src/UserService

# OR revert to a specific migration
dotnet ef database update PreviousMigrationName --project src/UserService --startup-project src/UserService
```

### CI/Pipeline Rollback
- If CI fails on data integration tests: check PostgreSQL container startup, verify connection string.
- If CI fails on health check: verify depends_on ordering (postgres must be healthy before UserService starts).

---

## Incident Triage

| Symptom | Check | Recovery |
|---------|-------|----------|
| PostgreSQL unhealthy | `docker compose logs postgres` | Check port 5432 availability, recreate with `docker compose down -v && docker compose up -d` |
| Migration fails | `dotnet ef database update --verbose` | Check connection string, verify PostgreSQL user exists |
| `role "kendo" does not exist` | `docker compose exec postgres psql -U kendo -d kendo_users -c "SELECT 1;"` | Recreate container with `docker compose down -v postgres && docker compose up -d postgres` |
| Port 5432 conflict | `lsof -i :5432` | Stop local PostgreSQL (`brew services stop postgresql@18`) or remap port |
| UserService can't connect | `docker compose logs userservice` | Verify `depends_on` postgres healthy, check connection string env var |
| Data volume lost | `docker volume ls \| grep pgdata` | Restore from backup or recreate: `docker compose down -v && docker compose up -d` |

---

## EF Core Commands Reference

```bash
# Add a new migration
dotnet ef migrations add <Name> --project src/UserService --startup-project src/UserService

# Apply pending migrations
dotnet ef database update --project src/UserService --startup-project src/UserService

# List applied migrations
dotnet ef migrations list --project src/UserService --startup-project src/UserService

# Generate SQL script (for DBA review)
dotnet ef migrations script --project src/UserService --startup-project src/UserService
```
