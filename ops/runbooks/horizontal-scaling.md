# Horizontal Scaling Event Playbook
> **Scenario:** Planned or reactive horizontal scaling of stateless service replicas (Gateway, UserService, Worker).  
> **Owners:** Platform Engineering / SRE  
> **Severity:** Low (planned) / Medium (reactive)

---

## Detection

### Planned Scaling
- Scheduled capacity increase (e.g., expected traffic spike from a campaign)
- Pre-deployment checklist item

### Reactive Scaling Triggers
1. **Rate limiter activation** — `RateLimitingMiddleware` begins returning 429 with `Retry-After` headers, indicating the current replica count cannot handle the request volume.
2. **Load shedding activation** — `LoadSheddingMiddleware` returns 503 RFC 7807 under extreme concurrency, suggesting replicas are saturated.
3. **CPU/memory thresholds** — Container metrics show sustained CPU > 80% or memory > 75% across all replicas.
4. **Request latency increase** — P95 response time exceeds target SLO (e.g., > 500ms for API endpoints).

---

## Prerequisites Check

Before scaling, verify all statelessness requirements from M3.2 are met:

```bash
# 1. Verify no sticky sessions
echo "Sticky session check: NGINX should NOT have 'ip_hash' or 'sticky' directives"
grep -E "ip_hash|sticky" ops/nginx/nginx.conf || echo " ✓ No sticky session directives found"

# 2. Verify replica identity middleware is working
curl -s -I http://localhost:5000/health/live 2>/dev/null | grep -i "X-Kendo-Replica"

# 3. Verify Redis is accessible (for distributed cache)
docker compose exec redis redis-cli ping 2>/dev/null || echo " ⚠ Redis unreachable — cache will use in-memory fallback"
```

---

## Scaling Procedure

### Step 1: Determine Scale Target

| Service | Min Replicas | Max Recommended | Scaling Basis |
|---|---|---|---|
| Gateway | 2 | 10 | Request volume, rate limiter activation |
| UserService | 2 | 10 | DB connection pool limits (max 100), request volume |
| Worker | 2 | 6 | Message queue depth, idempotency table throughput |

### Step 2: Update Replica Count

```bash
# Scale up (example: Gateway from 3 to 5)
docker compose up -d --scale gateway=5 --no-deps gateway

# Scale down (example: Worker from 4 to 2)
docker compose up -d --scale worker=2 --no-deps worker
```

> **Important:** Always scale one service at a time. Never scale multiple services in the same command — this makes it impossible to isolate issues.

### Step 3: Verify NGINX Upstream Re-registration

NGINX detects new replicas automatically via Docker DNS (service name resolution). Each new container is registered as an upstream server.

```bash
# Wait for all new replicas to become healthy
EXPECTED=$(docker compose ps --format json | grep -c '"Health":"healthy"')
for i in $(seq 1 12); do
  HEALTHY=$(docker compose ps --format json | grep '"Health":"healthy"' | wc -l)
  if [ "$HEALTHY" -eq "$EXPECTED" ]; then
    echo "All $EXPECTED containers healthy after ${i}s"
    break
  fi
  echo "wait ($i/12): $HEALTHY/$EXPECTED healthy"
  sleep 5
done
```

### Step 4: Verify Traffic Distribution

```bash
# Verify traffic is balanced across ALL replicas (including new ones)
echo "=== Traffic distribution (20 requests) ==="
declare -A REPLICA_MAP
for i in $(seq 1 20); do
  REPLICA=$(curl -s -I http://localhost:5000/health/live 2>/dev/null | grep -i "X-Kendo-Replica" | tr -d '\r' | cut -d' ' -f2)
  REPLICA_MAP["$REPLICA"]=$((REPLICA_MAP["$REPLICA"] + 1))
done
for r in "${!REPLICA_MAP[@]}"; do
  echo "  $r: ${REPLICA_MAP[$r]} requests (${REPLICA_MAP[$r]}%)"
done
echo "Total replicas seen: ${#REPLICA_MAP[@]}"
```

### Step 5: Load Test (Optional)

```bash
# Quick load test to verify scaling improved throughput
for i in $(seq 1 50); do
  STATUS=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:5000/api/users 2>/dev/null || echo "FAIL")
  if [ "$STATUS" != "200" ] && [ "$STATUS" != "429" ]; then
    echo "Unexpected status $STATUS at request $i"
  fi
done
echo "50 requests completed"
```

---

## Graceful Shutdown Drain Window Sizing

When scaling **down**, replicas being removed must complete in-flight requests before exiting. The drain window is configured via `GracefulShutdown__TimeoutSeconds` (default: 30s).

**Rule of thumb** for drain window:

| Avg Request Duration | Suggested Drain Window | Formula |
|---|---|---|
| < 100ms | 10s | 10s |
| 100ms–1s | 30s | Default |
| 1s–5s | 60s | 3× P99 latency |
| > 5s | 120s | 3× P99 latency |

To update the drain window:

```bash
# docker-compose.yml
services:
  gateway:
    environment:
      GracefulShutdown__TimeoutSeconds: 30
  userservice:
    environment:
      GracefulShutdown__TimeoutSeconds: 30
  worker:
    environment:
      GracefulShutdown__TimeoutSeconds: 30
```

> **Note:** Docker Compose `stop_grace_period` must exceed the `GracefulShutdown__TimeoutSeconds` value (see `day_15_runbook.md`).

---

## Statelessness Validation

After scaling, confirm no state leaked into in-process memory:

```bash
# 1. Check that no session state files exist in containers
for svc in gateway userservice worker; do
  docker compose exec "$svc" ls /app/App_Data 2>/dev/null && echo " ⚠ $svc has App_Data directory (state leak risk)"
done

# 2. Verify X-Kendo-Replica differs per request (no sticky session interference)
declare -A HEADERS
for i in $(seq 1 10); do
  H=$(curl -s -I http://localhost:5000/health/live 2>/dev/null | grep -i "X-Kendo-Replica" | tr -d '\r')
  HEADERS["$H"]=$((HEADERS["$H"] + 1))
done
if [ ${#HEADERS[@]} -ge 2 ]; then
  echo " ✓ Traffic distributed across ${#HEADERS[@]} replicas"
else
  echo " ⚠ Only ${#HEADERS[@]} replica seen — possible sticky session issue"
fi
```

---

## Redis Cache Warming

After scaling up, new replicas start with cold caches. Consider warming strategies for high-traffic paths:

### Automatic Warming (Recommended)
Redis is shared across all replicas — new replicas benefit from cache entries populated by existing replicas. No explicit warming needed for most workloads.

### Pre-Warming (For Critical High-Traffic Endpoints)

If a specific endpoint is expected to receive a traffic spike immediately after scaling:

```bash
# Pre-warm by hitting the endpoint a few times
for i in $(seq 1 5); do
  curl -s -o /dev/null http://localhost:5000/api/users
done
```

The cache will be populated after 1–2 requests. Subsequent requests benefit from cached data.

---

## Scale-Down Procedure

When scaling down, use `docker compose` with graceful shutdown:

```bash
# 1. Reduce replica count
docker compose up -d --scale gateway=2 --no-deps gateway

# 2. Monitor the drain — the extra replicas will finish in-flight requests
# and exit cleanly via the SIGTERM handler (see day_15_runbook.md)

# 3. Verify remaining replicas handle traffic
for i in $(seq 1 10); do
  STATUS=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:5000/health/live)
  if [ "$STATUS" != "200" ]; then
    echo " ⚠ Scale-down issue: status $STATUS"
    break
  fi
done
echo " ✓ Scale-down verified"
```

> **Important:** Never use `docker compose kill` to scale down — this bypasses the graceful shutdown drain window and may drop in-flight requests.

---

## Post-Mortem Checklist

- [ ] Scaling trigger documented (planned event / rate limiter / CPU threshold)
- [ ] All new replicas healthy and registered in NGINX upstream
- [ ] Traffic distribution verified across all replicas
- [ ] Graceful shutdown drain window appropriately sized
- [ ] Statelessness validated (no sticky sessions, no in-process state)
- [ ] Redis cache hit ratio stable (no cold-cache degradation)
- [ ] Rate limiter / load shedder thresholds reviewed post-scale
- [ ] Service-level SLOs met after scaling event
- [ ] CI pipeline passes with new replica count (if committed to config)

---

## Related Runbooks
- [`service-crash-recovery.md`](./service-crash-recovery.md) — if scale-up causes container crashes
- [`db-failover.md`](./db-failover.md) — if scale-up increases DB connection pressure
- [`day_14_runbook.md`](./day_14_runbook.md) — rate limiting & load shedding details
- [`day_15_runbook.md`](./day_15_runbook.md) — graceful shutdown configuration
- [`day_16_runbook.md`](./day_16_runbook.md) — central recovery index
