# Day 13 — Chaos Test Runbook

> Runbook for Day 13 of the Kendo platform: M3.3 Chaos Test Suite

## Prerequisites

### Required
- Docker Desktop / Docker Engine running
- `docker compose up -d --scale gateway=3 --scale userservice=3 --scale worker=3` running
- All 12 containers healthy (3 gateway + 3 userservice + 3 worker + nginx + postgres + redis)

### Optional
- Azure Service Bus connection string (`Rebus__ConnectionString`) for network partition test

## Running Chaos Tests

### Option 1 — Bash scripts (CI-style integration tests)

```bash
chmod +x scripts/chaos/*.sh
scripts/chaos/run_all.sh
```

Individual scripts:
```bash
scripts/chaos/test_db_downtime.sh       # Pause PostgreSQL, verify circuit breaker
scripts/chaos/test_service_crash.sh     # Kill a replica, verify NGINX re-route
scripts/chaos/test_network_partition.sh # Verify outbox + Rebus async path
```

### Option 2 — xUnit tests

```bash
dotnet test tests/Kendo.Tests --filter "Category=Chaos"
```

> Note: xUnit chaos tests require Docker environment and will auto-skip otherwise.

## What Each Test Validates

### DB Downtime (test_db_downtime.sh)
1. Pause PostgreSQL container
2. Circuit breaker trips after 3 consecutive failures
3. UserService returns 503 with ProblemDetails fallback body
4. Gateway continues serving (no cascading failure to upstream)
5. Unpause PostgreSQL -> system recovers

### Service Crash (test_service_crash.sh)
1. Kill one user-service replica container
2. NGINX health check detects the failure
3. Remaining replicas absorb all traffic
4. All requests through NGINX return 200
5. X-Kendo-Replica headers visible from remaining replicas
6. Restart killed replica -> full recovery

### Network Partition (test_network_partition.sh)
1. POST /api/users returns 202 Accepted with Location header
2. Outbox message created and relay processes it
3. Status endpoint returns 200 once async processing completes
4. If broker not available (local dev): test skips gracefully

## Expected Results

| Test | Expected | Failure Mode |
|---|---|---|
| DB downtime | Circuit breaker -> 503 fallback -> 200 recovery | Circuit breaker not configured, no fallback handler |
| Service crash | 200 all requests -> replica headers present | NGINX health check TTL too long, no replica diversity |
| Network partition | 202 -> status 200 | ASB not configured, outbox relay not running |

## Recovery Playbook

| Scenario | Action | Expected Recovery Time |
|---|---|---|
| PostgreSQL crash | `docker compose up -d postgres` | ~5s (health check interval) |
| Service crash | `docker compose up -d --scale gateway=3 --scale userservice=3 --scale worker=3` | ~8s (NGINX health check TTL) |
| Network partition | Restart broker or redeploy with correct connection string | Varies (outbox relay polls every 5s) |
