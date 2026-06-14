# Service Crash Recovery Playbook
> **Scenario:** One or more service replicas crash or become unhealthy (Gateway, UserService, or Worker).  
> **Owners:** Platform Engineering / SRE  
> **Severity:** High — partial service degradation until replicas recover

---

## Detection

### Automated Signals
1. **NGINX health check failure** — NGINX marks the unhealthy replica as down after the `max_fails` threshold (default: 1 failure) within the `fail_timeout` window (default: 10s).
2. **Docker container exit** — `docker compose ps` shows the replica as `Exited (xxx)` or `unhealthy`.
3. **X-Kendo-Replica header change** — Traffic distribution shifts: all requests from a single replica disappear, indicating one is down.
4. **502 Bad Gateway** — If all replicas of a service are down, NGINX returns `502 Bad Gateway`.

### Manual Signals
- Users report intermittent errors or timeouts.
- Dashboard shows replica count drop for a service.

---

## Triage Steps

```bash
# 1. Check all container statuses
docker compose ps

# 2. Identify crashed replica
docker compose ps --format "table {{.Name}}\t{{.Status}}\t{{.Health}}" | grep -v "healthy"

# 3. Check logs for crash reason
docker compose logs --tail=50 <container-name>

# 4. Verify NGINX upstream status
curl -s -o /dev/null -w "Gateway via NGINX: %{http_code}\n" http://localhost:5000/health/live
curl -s http://localhost:5000/nginx-health 2>/dev/null
```

---

## Recovery Procedure

### Step 1: Restart the Crashed Service

```bash
# Restart a single service (keeps other replicas running)
docker compose up -d --no-deps --scale <service>=<current+1> <service>
# Example for UserService with 3 replicas:
docker compose up -d --no-deps --scale userservice=3 userservice
```

Or, for a full service restart:

```bash
docker compose restart <service>
```

### Step 2: Wait for Health Check Re-convergence

NGINX health checking operates on these timings:
- **Health check interval:** Every 5s (default)
- **Fail timeout:** 10s (default) — NGINX stops routing to a failed replica after **1 failed request**
- **Recovery timeout:** After NGINX marks a replica as failed, it waits `fail_timeout` (10s) before retrying

```bash
# Wait for the new replica to become healthy
for i in $(seq 1 12); do
  healthy=$(docker compose ps --format json | grep '"Health":"healthy"' | wc -l)
  if echo "$healthy" | grep -qE "^(6|12)$"; then
    echo "All containers healthy ($healthy) — recovered"
    break
  fi
  echo "wait ($i/12): $healthy containers healthy"
  sleep 5
done
```

### Step 3: Verify Traffic Distribution

```bash
# Verify traffic is load-balanced across all replicas
echo "=== Traffic distribution (10 requests) ==="
for i in $(seq 1 10); do
  REPLICA=$(curl -s -I http://localhost:5000/health/live 2>/dev/null | grep -i "X-Kendo-Replica" | tr -d '\r' | cut -d' ' -f2)
  echo "  Request $i: $REPLICA"
done
# Expected: at least 2 different replica hostnames visible
```

### Step 4: Full Smoke Test

```bash
# Health endpoints
curl -fsS http://localhost:5000/health/live && echo " ✓ Gateway via NGINX"
curl -fsS http://localhost:5000/health/ready && echo " ✓ Gateway ready via NGINX"

# Business endpoint through NGINX
curl -s -o /dev/null -w "GET /api/users: %{http_code}\n" http://localhost:5000/api/users

# Direct service health
curl -fsS http://localhost:5001/health/live && echo " ✓ UserService direct"
curl -fsS http://localhost:5002/health/live && echo " ✓ Worker direct"
```

---

## Rollback Sequence (Failed Redeployment)

If a service redeployment caused the crash:

```bash
# 1. Identify the previously working image tag
docker images | grep kendo

# 2. Revert docker-compose.yml to the previous image tag
# Edit docker-compose.yml and change the image tag

# 3. Redeploy with the previous image
docker compose up -d --no-deps <service>

# 4. Verify rollback succeeded
docker compose ps | grep <service>
curl -fsS http://localhost:5001/health/live && echo " ✓ Service healthy after rollback"
```

---

## Multi-Replica Crash (All Replicas Down)

If all replicas of a service crash simultaneously:

```bash
# 1. Restart the service with fresh replicas
docker compose up -d --scale <service>=3 --no-deps <service>

# 2. Monitor NGINX upstream — should auto-detect recovery
for i in $(seq 1 15); do
  STATUS=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:5000/health/live 2>/dev/null || echo "000")
  if [ "$STATUS" = "200" ]; then
    echo "NGINX upstream recovered after ${i}s"
    break
  fi
  sleep 2
done

# 3. Verify all replicas are healthy
docker compose ps
```

> **Recovery budget:** 8 seconds (NGINX health check TTL) + 10s (Docker restart) ≈ 18s expected recovery time for a single replica crash. Multi-replica crash adds container startup time (~5s per replica).

---

## Post-Mortem Checklist

- [ ] Root cause identified (OOM, unhandled exception, config error, resource exhaustion)
- [ ] All containers restarted and healthy
- [ ] NGINX traffic distribution verified (≥ 2 distinct replica hostnames)
- [ ] Business endpoints responding correctly through NGINX
- [ ] Rate limiter counters reset (if Gateway restarted)
- [ ] Outbox relay running (if Worker restarted — check Worker logs)
- [ ] Graceful shutdown drain window confirmed adequate (see `day_15_runbook.md`)
- [ ] Auto-scaling / replica count restored to configured minimum
- [ ] Runbook updated with any new recovery steps discovered

---

## Related Runbooks
- [`db-failover.md`](./db-failover.md) — for DB-level crashes (not service)
- [`dlq-drain.md`](./dlq-drain.md) — for message backlog recovery after crash
- [`horizontal-scaling.md`](./horizontal-scaling.md) — for planned scaling events
- [`day_15_runbook.md`](./day_15_runbook.md) — graceful shutdown details
- [`day_16_runbook.md`](./day_16_runbook.md) — central recovery index
