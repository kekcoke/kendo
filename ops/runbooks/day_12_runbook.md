# Day 12 — NGINX Reverse Proxy & Multi-Replica Runbook
> **Milestone:** M3.1 — Load balancer: reverse proxy (NGINX) routing traffic across ≥ 2 container replicas per service  
> **Services affected:** nginx, gateway (3x), userservice (3x), worker (3x), postgres  
> **Last updated:** 2026-06-13

---

## Topology

```
                     ┌──▶ gateway:5000 (replica 1)
                     ├──▶ gateway:5000 (replica 2)
Client ──▶ nginx:80 ──┼──▶ gateway:5000 (replica 3)
          (port 5000) │
                     ├──▶ userservice:5001 (replica 1)
                     ├──▶ userservice:5001 (replica 2)
                     └──▶ userservice:5001 (replica 3)
```

- **Single entry point:** `localhost:5000` → NGINX (port 80 internal)
- **Backend services:** no host port exposure — Docker DNS only (`expose:`)
- **Worker (x3):** internal async consumer, not exposed through NGINX
- **Postgres:** single replica, unchanged from Phase 02

---

## Health & Observability Contract

### Health Check Endpoints

| Endpoint | Target | Expected Response | Used By |
|---|---|---|---|
| `GET /health/live` | Gateway (via NGINX) | `200 "Healthy"` | Docker health check, CI smoke test |
| `GET /health/ready` | Gateway (via NGINX) | `200 "Ready"` | CI smoke test |
| `GET /health/live` (direct) | each backend service | `200 "Healthy"` | NGINX passive health check |
| `POST /api/users` (via NGINX) | UserService | `202 Accepted` + `Location` header | CI smoke test |

### What to Verify After Deployment

1. **All 11 containers healthy:**
   ```bash
   docker compose ps --format json | grep '"Health":"healthy"' | wc -l
   # Expected: 11
   ```

2. **NGINX proxies traffic correctly:**
   ```bash
   curl -fsS http://localhost:5000/health/live        # → 200 through NGINX
   curl -fsS -o /dev/null -w "%{http_code}" -X POST \
     http://localhost:5000/api/users \
     -H 'Content-Type: application/json' \
     -d '{"email":"verify@test.com","displayName":"Verify"}'  # → 202
   ```

3. **No direct backend access:**
   ```bash
   curl -s -o /dev/null -w "%{http_code}" http://localhost:5001/health/live
   # Expected: connection refused (ports not exposed on host)
   ```

4. **Traffic distributes across replicas:**
   ```bash
   # Run 20 requests and check no 502 errors from load balancer
   for i in $(seq 1 20); do
     STATUS=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:5000/health/live)
     if [ "$STATUS" = "502" ]; then echo "FAIL: 502 on request $i"; exit 1; fi
   done
   echo "All 20 requests succeeded"
   ```

### Key Log Patterns

| Component | Log Pattern | Meaning |
|---|---|---|
| NGINX | `GET /health/live HTTP/1.1" 200` | Normal health check proxied to upstream |
| NGINX | `GET /api/users HTTP/1.1" 202` | User API call proxied to userservice upstream |
| NGINX | `no live upstreams` | All replicas in an upstream group are down |
| NGINX | `upstream timed out (110: Connection timed out)` | Backend replica unreachable |
| Gateway | `/health/live responded 200` | Gateway health check passing |
| Gateway | `Circuit breaker is open` | All upstream replicas unhealthy (existing resilience) |

---

## Deployment & Rollback Playbook

### Deploying the Feature

1. **Pull latest feature branch:**
   ```bash
   git checkout feature/day-12-reverse-proxy-load-balancer
   git pull origin feature/day-12-reverse-proxy-load-balancer
   ```

2. **Build images:**
   ```bash
   docker compose build
   ```

3. **Start single-replica baseline (backward compatibility check):**
   ```bash
   docker compose up -d
   # Verify: 4 containers (gateway, userservice, worker, postgres) healthy via old direct ports
   ```

4. **Tear down and start multi-replica:**
   ```bash
   docker compose down
   docker compose up -d --scale gateway=3 --scale userservice=3 --scale worker=3
   ```

5. **Wait for all 11 containers healthy:**
   ```bash
   for i in $(seq 1 20); do
     HEALTHY=$(docker compose ps --format json | grep '"Health":"healthy"' | wc -l)
     [ "$HEALTHY" -ge 11 ] && echo "All 11 healthy" && break
     sleep 4
   done
   ```

6. **Run smoke tests:**
   ```bash
   # Health through NGINX
   curl -fsS http://localhost:5000/health/live
   
   # POST through NGINX to UserService
   curl -s -o /dev/null -w "%{http_code}" -X POST \
     http://localhost:5000/api/users \
     -H 'Content-Type: application/json' \
     -d '{"email":"deploy-verify@test.com","displayName":"Deploy Verify"}' | grep -q 202 && echo "202 OK"
   
   # Traffic distribution
   for i in $(seq 1 10); do
     curl -s -o /dev/null -w " %{http_code}" http://localhost:5000/health/live
   done
   echo ""
   ```

7. **Run infrastructure integration tests (optional, requires Docker):**
   ```bash
   dotnet test tests/Kendo.Tests --filter Category=Infrastructure --no-restore
   ```

### Rolling Back

**Rollback strategy:** Three tiers depending on severity.

#### Tier 1 — Quick rollback (feature branch still available)
> Use when multi-replica config is broken but single-replica is fine.

```bash
# Stop multi-replica
docker compose down

# Revert docker-compose.yml to Day 11 state
git checkout develop -- docker-compose.yml

# Restart single-replica (back to direct ports)
docker compose up -d

# Verify direct port access works
curl -fsS http://localhost:5000/health/live
curl -fsS http://localhost:5001/health/live
curl -fsS http://localhost:5002/health/live
```

#### Tier 2 — Full feature revert
> Use when the entire NGINX topology needs to be removed.

```bash
# Revert all Day 12 files
docker compose down
git checkout develop -- docker-compose.yml ops/nginx/ nginx.conf

# If nginx config was committed as a new file, remove it
git rm -r ops/nginx/

# Restart single-replica baseline
docker compose up -d
```

#### Tier 3 — Merge revert (worst case, PR already merged)
> Use if Day 12 was merged and must be undone.

```bash
# Find the merge commit
git log --oneline develop | head -5

# Revert the merge
git revert -m 1 <MERGE_COMMIT_SHA>
git push origin develop
```

---

## Failure Scenarios

### Scenario 1: NGINX fails to start

**Symptoms:**
- `docker compose ps` shows nginx in `Exit` or `Restarting` state
- `curl http://localhost:5000` returns connection refused

**Checklist:**
```bash
# Check NGINX config syntax inside container
docker compose exec nginx nginx -t

# View container logs
docker compose logs nginx

# Verify nginx.conf is mounted correctly
docker compose exec nginx cat /etc/nginx/nginx.conf
```

**Recovery:**
1. Fix nginx.conf syntax error
2. `docker compose up -d --no-recreate nginx`
3. If config is valid but service still fails, check if ports are already in use:
   ```bash
   lsof -i :5000
   ```

### Scenario 2: Backend replicas out of sync

**Symptoms:**
- All containers are healthy, but NGINX returns 502
- Some requests succeed, others fail intermittently

**Checklist:**
```bash
# Check if backend services are reachable from inside NGINX
docker compose exec nginx curl -s http://gateway:5000/health/live
docker compose exec nginx curl -s http://userservice:5001/health/live

# Check DNS resolution inside NGINX container
docker compose exec nginx getent hosts gateway
# Expected: multiple IPs for each replica
```

**Recovery:**
1. `docker compose restart nginx` (flushes DNS cache)
2. If DNS still has stale entries, `docker compose down` and restart fresh

### Scenario 3: Replica kill causes client-visible errors

**Symptoms:**
- CI test `KillOneReplica_NoClientVisibleErrors` fails
- Clients report 502 or 503 during a replica restart

**Checklist:**
```bash
# Check NGINX health check timing
docker compose logs nginx --tail 50 | grep -E "no live|timeout|502|503"

# Verify max_fails and fail_timeout settings
docker compose exec nginx cat /etc/nginx/nginx.conf
```

**Recovery:**
1. Increase `max_fails` or `fail_timeout` in nginx.conf upstream blocks
2. Verify Docker health check interval is faster than NGINX fail timeout
3. If issue persists, consider adding active health checks (`health_check` directive with NGINX Plus or a custom endpoint)

---

## Rollback to Day 11 Baseline

If the NGINX topology must be abandoned:

```bash
# 1. Stop everything
docker compose down

# 2. Restore docker-compose.yml to Day 11 (direct ports)
git checkout develop -- docker-compose.yml

# 3. Remove NGINX config (committed new file)
git rm -r ops/nginx/

# 4. Remove infrastructure tests (committed new file)
git rm tests/Kendo.Tests/Infrastructure/LoadBalancerTests.cs

# 5. Revert CI pipeline changes
git checkout develop -- .github/workflows/ci.yml

# 6. Restart baseline
docker compose up -d

# 7. Verify direct port access
curl -fsS http://localhost:5000/health/live
curl -fsS http://localhost:5001/health/live
curl -fsS http://localhost:5002/health/live
echo "Day 11 baseline restored"
```
