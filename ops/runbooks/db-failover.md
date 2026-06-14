# DB Failover Recovery Playbook
> **Scenario:** PostgreSQL database crash, corruption, or connection loss during operation.  
> **Owners:** Platform Engineering / SRE  
> **Severity:** Critical — cascading service degradation if unresolved

---

## Detection

### Automated Signals
1. **Circuit breaker trips** — Polly circuit breaker on `UserService` DB client opens after 3 consecutive failures. Logged at `Error` level with `Event: CircuitBreakerOpen`.
2. **503 responses** — `UserService` returns `503 Service Unavailable` with RFC 7807 body containing `"database_unavailable"` detail.
3. **Health check transition** — `/health/ready` on `UserService` transitions from healthy to unhealthy (DB connectivity check fails).
4. **Docker health check** — `docker compose ps` shows `UserService` container health status as `unhealthy` or `starting`.

### Manual Signals
- Gateway returns 500/503 on user-related endpoints.
- Grafana dashboard shows sudden spike in DB connection errors.
- Alert from CI chaos test (`test_db_downtime`) — see [Known CI Flakiness](#known-ci-flakiness-test_db_downtime).

---

## Triage Steps

```bash
# 1. Check PostgreSQL container status
docker compose ps postgres

# 2. Check PostgreSQL logs
docker compose logs --tail=50 postgres

# 3. Check UserService circuit breaker status
docker compose logs --tail=30 userservice | grep "CircuitBreaker\|database_unavailable"

# 4. Verify service health
curl -s -o /dev/null -w "UserService health: %{http_code}\n" http://localhost:5001/health/ready
curl -s -o /dev/null -w "Gateway health: %{http_code}\n" http://localhost:5000/health/ready
```

> **Note:** Gateway continues serving non-user traffic even during DB outage — this is by design (no cascading failure).

---

## Recovery Procedure

### Step 1: Restart PostgreSQL

```bash
docker compose restart postgres
# or if the container needs full recreation:
docker compose up -d --force-recreate postgres
```

### Step 2: Wait for Database Recovery

```bash
# Wait for pg_isready
for i in $(seq 1 12); do
  if docker compose exec postgres pg_isready -U kendo -d kendo_users > /dev/null 2>&1; then
    echo "PostgreSQL healthy after ${i}s"
    break
  fi
  sleep 5
done
```

### Step 3: Verify Circuit Breaker Reset

The Polly circuit breaker automatically half-opens after the configured break duration (default: 30s) and closes on the first successful probe request.

```bash
# Trigger a probe request to reset the circuit breaker
curl -s http://localhost:5001/api/users 2>/dev/null | head -c 200
echo ""

# Confirm healthy response
curl -s -o /dev/null -w "UserService response: %{http_code}\n" http://localhost:5001/health/ready
```

### Step 4: Verify Full System Health

```bash
# All services healthy?
docker compose ps | grep -c "healthy"  # Expected: 6 (gateway, userservice, worker, postgres, nginx, redis)

# Health endpoints through NGINX
curl -fsS http://localhost:5000/health/live && echo " ✓ Gateway via NGINX"
curl -fsS http://localhost:5001/health/live && echo " ✓ UserService"
curl -fsS http://localhost:5002/health/live && echo " ✓ Worker"

# Smoke test — create a user (triggers DB write + outbox)
curl -s -o /dev/null -w "POST /api/users: %{http_code}\n" \
  -X POST http://localhost:5000/api/users \
  -H "Content-Type: application/json" \
  -d '{"username":"recovery-test","email":"recovery@test.com"}'
```

### Step 5: Monitor for Recurrence

```bash
# Stream logs for circuit breaker activity
docker compose logs --tail=100 --follow userservice | grep --line-buffered "CircuitBreaker\|Polly"
```

---

## Connection String Rotation

If the DB crash was caused by credential issues or you need to rotate the connection string:

1. Stop UserService:
   ```bash
   docker compose stop userservice
   ```

2. Update the environment variable in `docker-compose.yml`:
   ```yaml
   services:
     userservice:
       environment:
         ConnectionStrings__DefaultConnection: "Host=new-host;Port=5432;Database=kendo_users;Username=new_user;Password=new_password"
   ```

3. Restart UserService:
   ```bash
   docker compose up -d userservice
   ```

4. Wait for health check to pass and verify Circuit Breaker closes.

---

## Post-Mortem Checklist

- [ ] Root cause identified and documented
- [ ] Connection string rotated (if credential-related)
- [ ] Circuit breaker reset verified with probe request
- [ ] All 6 containers healthy (`docker compose ps`)
- [ ] NGINX traffic distribution verified (run 10 requests through gateway)
- [ ] Outbox relay resumed processing pending messages (check Worker logs)
- [ ] No data loss confirmed (recent records queryable in DB)

---

## Known CI Flakiness: `test_db_downtime`

### Observed Symptom
On PR #19 CI run (Day 15), the chaos test `test_db_downtime` intermittently returned `000000` (connection refused) instead of the expected `503` after PostgreSQL was unpaused.

### Root Cause (Suspected)
Race condition between PostgreSQL container resume and the Docker health check re-convergence in CI. The bash script's `docker compose unpause postgres` returns before the container's `pg_isready` health check passes, causing the follow-up `curl` request to hit the service before the DB connection pool reconnects.

### Affected Environments
- CI (`ubuntu-latest` GitHub Actions runner) — observed once on PR #19
- Local Docker Desktop — not reproduced

### Recommended Mitigation
1. Increase `--health-retries` in `.github/workflows/ci.yml` for the PostgreSQL service container from 5 to 8.
2. Add a retry loop in `scripts/chaos/test_db_downtime.sh` after `docker compose unpause`:
   ```bash
   for i in $(seq 1 10); do
     if docker compose exec postgres pg_isready -U kendo -d kendo_users > /dev/null 2>&1; then
       break
     fi
     sleep 2
   done
   ```
3. Alternatively, increase the `--health-interval` from 5s to 10s for PostgreSQL to allow more recovery time.

### Tracking
- Carry-forward from Day 13 → Day 15 → Day 16
- Marked as **deferred** — root cause must be confirmed before fix is applied
- Consider adding a `sleep 5` after `docker compose unpause` as a short-term patch

---

## Related Runbooks
- [`service-crash-recovery.md`](./service-crash-recovery.md) — for service-level crashes (not DB)
- [`day_13_runbook.md`](./day_13_runbook.md) — chaos test execution guide
- [`day_16_runbook.md`](./day_16_runbook.md) — central recovery index
