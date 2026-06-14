# Day 12 — Stateless Validation & Redis Distributed Cache Runbook
> **Milestone:** M3.2 — Stateless validation: sticky sessions disabled; any session state externalized to Redis or the database  
> **Services affected:** gateway, userservice, worker, redis, nginx, postgres  
> **Last updated:** 2026-06-14

---

## Topology

```
                     ┌──▶ gateway:5000 (replica 1) ── Redis:6379
                     ├──▶ gateway:5000 (replica 2) ── Redis:6379
Client ──▶ nginx:80 ──┼──▶ gateway:5000 (replica 3) ── Redis:6379
          (port 5000) │
                     ├──▶ userservice:5001 (replica 1) ── Redis:6379
                     ├──▶ userservice:5001 (replica 2) ── Redis:6379
                     └──▶ userservice:5001 (replica 3) ── Redis:6379
                      
                     worker:5002 (replica 1-3, internal) ── Redis:6379
                     
                     postgres:5432 (single, persistent state)
```

- **All services** have `IDistributedCache` registered — Redis when available, in-memory fallback otherwise.
- **Replica identity:** Every HTTP response carries `X-Kendo-Replica` header set to the Docker container hostname.
- **NGINX:** No sticky sessions. Round-robin distributes traffic across all healthy replicas. `X-Kendo-Replica` header is passed through to the caller.
- **Redis:** Externalized cache/session store. Best-effort — application continues without it.

---

## What Changed This Session

| Component | Change |
|---|---|
| `Kendo.Shared` | `AddKendoDistributedCache()` — conditional Redis/Memory registration. `ReplicaIdentityMiddleware` — sets `X-Kendo-Replica` header. |
| Gateway/UserService/Worker | All three wired with Redis cache registration + replica identity middleware. |
| `docker-compose.yml` | New Redis service (redis:7-alpine). `Redis__ConnectionString` env var on all 3 services. |
| `ops/nginx/nginx.conf` | `proxy_set_header X-Kendo-Replica $upstream_http_x_kendo_replica;` passthrough. |
| CI pipeline | Redis service container added. Health count updated (6 single-replica, 12 multi-replica). Replica header verification step added. |
| Tests | 7 new unit tests covering replica identity middleware (3) and cache registration (4). |

---

## Health & Observability Contract

### Health Check Endpoints

| Endpoint | Target | Expected Response | Used By |
|---|---|---|---|
| `GET /health/live` | All services | `200 "Healthy"` | Docker health check, CI smoke test |
| `GET /health/ready` | All services | `200 "Ready"` | CI smoke test |
| `PING` (redis-cli) | Redis | `PONG` | Docker health check |

### Key Response Headers

| Header | Source | Example | Purpose |
|---|---|---|---|
| `X-Kendo-Replica` | all 3 services | `"gateway-1"`, `"userservice-2"`, `"worker-3"` | Identify which container replica served the request |

### What to Verify After Deployment

1. **All containers healthy:**
   ```bash
   docker compose ps --format json | grep '"Health":"healthy"' | wc -l
   # Single-replica: 6 (gateway, userservice, worker, postgres, nginx, redis)
   # Multi-replica: 12 (3 gateway + 3 userservice + 3 worker + nginx + postgres + redis)
   ```

2. **X-Kendo-Replica header present through NGINX:**
   ```bash
   curl -sI http://localhost:5000/health/live | grep -i X-Kendo-Replica
   # Expected: X-Kendo-Replica: <container-hostname>
   ```

3. **Multiple requests show different replica IDs:**
   ```bash
   for i in $(seq 1 10); do
     curl -sI http://localhost:5000/health/live 2>/dev/null | grep -i X-Kendo-Replica
   done
   # Expected: at least 2 different hostnames across 10 requests
   ```

4. **IDistributedCache is available:**
   ```bash
   # Injected into all 3 services — verified via unit tests
   dotnet test tests/Kendo.Tests --filter "Category=Caching" --no-build
   ```

### Key Log Patterns

| Component | Log Pattern | Meaning |
|---|---|---|
| Any service | `Replica identity resolved: gateway-1` | Replica resolved at startup |
| Any service | `Redis connection failed, falling back to memory cache` | Redis unavailable, graceful degrade |
| NGINX | `X-Kendo-Replica: gateway-1` in access logs | Replica header passed through |

---

## Deployment & Rollback Playbook

### Deploying the Feature

1. **Pull latest feature branch:**
   ```bash
   git checkout feature/day-12-stateless-validation
   git pull origin feature/day-12-stateless-validation
   ```

2. **Build images:**
   ```bash
   docker compose build
   ```

3. **Start services:**
   ```bash
   docker compose up -d
   ```

4. **Wait for all 6 services healthy:**
   ```bash
   for i in $(seq 1 20); do
     HEALTHY=$(docker compose ps --format json | grep '"Health":"healthy"' | wc -l)
     [ "$HEALTHY" -ge 6 ] && echo "All 6 healthy" && break
     sleep 4
   done
   ```

5. **Run smoke tests:**
   ```bash
   # Health through NGINX
   curl -fsS http://localhost:5000/health/live
   
   # Replica identity header verification
   curl -sI http://localhost:5000/health/live
   
   # Redis is available to all services
   docker compose exec redis redis-cli ping
   
   # POST through NGINX to UserService
   curl -s -o /dev/null -w "%{http_code}" -X POST \
     http://localhost:5000/api/users \
     -H 'Content-Type: application/json' \
     -d '{"email":"deploy-verify@test.com","displayName":"Deploy Verify"}'
   ```

6. **Run unit tests:**
   ```bash
   dotnet test tests/Kendo.Tests --filter "Category=Unit" --no-build
   ```

### Rolling Back

**Rollback strategy:** Two tiers depending on severity.

#### Tier 1 — Quick rollback (minor issue)
> Use when Redis is causing issues but statelessness is fine.

```bash
# Simply remove Redis connection string from docker-compose.yml
# All services will fall back to in-memory cache automatically
docker compose up -d --no-recreate
```

#### Tier 2 — Full feature revert
> Use when replica identity middleware or Redis wiring causes problems.

```bash
# Revert all Day 12 files
docker compose down
git checkout develop -- src/Gateway/Program.cs src/Gateway/appsettings.json
git checkout develop -- src/UserService/Program.cs src/UserService/appsettings.json
git checkout develop -- src/Worker/Program.cs src/Worker/appsettings.json
git checkout develop -- docker-compose.yml ops/nginx/nginx.conf
git checkout develop -- .github/workflows/ci.yml

# Remove new files (if committed)
git rm -r src/Shared/Caching/
git rm tests/Kendo.Tests/Caching/ReplicaIdentityMiddlewareTests.cs
git rm tests/Kendo.Tests/Caching/DistributedCacheRegistrationTests.cs

# Restart
docker compose up -d
```

---

## Failure Scenarios

### Scenario 1: Redis is unreachable

**Symptoms:**
- `docker compose ps` shows redis in unhealthy state
- Logs show `Redis connection failed, falling back to memory cache`

**Impact:** None — application continues using in-memory cache. No data loss because cache is best-effort (optimisation, not correctness).

**Recovery:**
```bash
# Check Redis logs
docker compose logs redis

# Restart Redis
docker compose restart redis

# Verify Redis is healthy
docker compose exec redis redis-cli ping  # → PONG
```

### Scenario 2: X-Kendo-Replica header not visible

**Symptoms:**
- `curl -I` shows no `X-Kendo-Replica` header
- CI replica identity verification step fails

**Checklist:**
```bash
# Check header direct from service (bypassing NGINX)
docker compose exec userservice curl -sI http://localhost:5001/health/live | grep -i X-Kendo-Replica

# Check if NGINX passthrough is configured
docker compose exec nginx cat /etc/nginx/nginx.conf | grep X-Kendo-Replica
```

**Recovery:**
1. If header is missing at the service → check `ReplicaIdentityMiddleware` registration in `Program.cs`
2. If header is present at service but not through NGINX → verify `proxy_set_header X-Kendo-Replica $upstream_http_x_kendo_replica;` in nginx.conf
3. `docker compose restart nginx` to apply config changes

### Scenario 3: Multiple replicas return the same replica ID (sticky routing)

**Symptoms:**
- All 10 requests through NGINX return the same X-Kendo-Replica value
- Traffic not distributing across replicas

**Checklist:**
```bash
# Verify NGINX has no ip_hash or sticky directive
docker compose exec nginx cat /etc/nginx/nginx.conf

# Verify multiple replicas are registered in Docker DNS
docker compose exec nginx getent hosts gateway | wc -l
# Expected: 3 (for --scale gateway=3)
```

**Recovery:**
1. Ensure `--scale` flags are used: `docker compose up -d --scale gateway=3 --scale userservice=3 --scale worker=3`
2. Verify no `ip_hash` in nginx.conf upstream blocks
3. `docker compose restart nginx`

---

## Redis Cache Semantics

| Aspect | Behaviour |
|---|---|
| Registration | `AddStackExchangeRedisCache()` when `Redis__ConnectionString` is set; `AddDistributedMemoryCache()` otherwise |
| Instance prefix | `kendo:` — all Redis keys are namespaced |
| Failure mode | Cache miss returns `null` — callers must handle gracefully |
| Persistence | Best-effort in-memory cache (Redis is ephemeral for this milestone) |
| Connection string | `Redis__ConnectionString=redis:6379` (Docker Compose), `localhost:6379` (local dev) |
