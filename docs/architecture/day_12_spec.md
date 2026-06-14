# Day 12 — Architecture Specification

## Milestone Scope

**Milestone:** M3.1 — Load balancer: reverse proxy (NGINX) routing traffic across ≥ 2 container replicas per service
**Roadmap phase:** 03 — High Availability & Chaos Testing
**SLUG:** `reverse-proxy-load-balancer`

**Components touched:**
- **Docker Compose** (`docker-compose.yml`) — add NGINX reverse proxy service; remove direct port exposure of backend services; enable `--scale` compatibility
- **Ops bundle** (`ops/nginx/nginx.conf`) — new NGINX configuration with upstream load balancing per service, health-check-aware routing, and common proxy headers
- **Gateway** — no code changes; port exposure moves behind proxy
- **UserService** — no code changes; port exposure moves behind proxy
- **Worker** — no code changes; port exposure moves behind proxy
- **CI pipeline** (`.github/workflows/ci.yml`) — add multi-replica startup verification step
- **Dockerfiles** — no changes needed (existing health endpoints sufficient)

**Explicitly out of scope:**
- M3.2 **Stateless validation** (Redis session store, sticky-session disable) — deferred to next milestone
- M3.3 **Chaos test suite** (DB downtime, crash, partition scenarios) — deferred; needs load balancer first as a prerequisite
- M3.4 **Rate limiting & shedding** (429/503 enforcement at Gateway) — deferred; follows stateless validation
- M3.5 **Graceful shutdown** (SIGTERM handlers, drain timeout) — deferred; needs load balancer health-check unregistration
- M3.6 **Ops runbooks** — deferred until all failure scenarios are built

---

## Layer Changes

### Docker Compose — Topology Redesign

**Current topology (Day 11):**
```
Client ──▶ gateway:5000 ──▶ userservice:5001
                     └──▶ worker:5002
```

Each service is directly exposed on a host port. No load balancing. Single replica per service.

**Target topology (Day 12):**
```
                     ┌──▶ gateway:5000 (replica 1)
                     ├──▶ gateway:5000 (replica 2)
Client ──▶ nginx:80 ──┼──▶ gateway:5000 (replica 3)
                     │
                     ├──▶ userservice:5001 (replica 1)
                     ├──▶ userservice:5001 (replica 2)
                     └──▶ userservice:5001 (replica 3)
```

- **Single entry point:** NGINX on port 80 (maps to host port 5000 for backward-compatibility)
- **Backend services:** no host port mapping (`expose: [port]` only) — reachable only via Docker internal DNS
- **Worker:** not exposed externally via NGINX (internal async consumer); remains reachable via Docker DNS for health checks
- **Health-check routing:** NGINX `health_check` upstream directive ensures unhealthy replicas are removed from rotation

### NGINX Configuration — `ops/nginx/nginx.conf`

**Upstream blocks** (Docker DNS round-robin):
- `backend_gateway` — routes `gateway:5000` (3 replicas via `--scale`)
- `backend_userservice` — routes `userservice:5001` (3 replicas via `--scale`)

**Location routing:**
- `/api/users/*` → `backend_userservice`
- `/health/*` → both upstreams (health probes pass through)
- All other paths → `backend_gateway`

**Proxy settings:**
- Standard proxy headers: `X-Real-IP`, `X-Forwarded-For`, `X-Forwarded-Proto`, `Host`
- Proxy timeouts: connect=5s, read=30s, send=30s
- Client max body size: 10MB (same as existing Gateway limit)
- No sticky sessions — round-robin only (enforces statelessness, aligns with `## Architectural Decisions Log` entry: "no in-process session state permitted")

### Multi-Replica Strategy

Docker Compose supports per-service scaling via `--scale` CLI flag:
```bash
docker compose up -d --scale gateway=3 --scale userservice=3 --scale worker=3
```

NGINX automatically routes to all healthy replicas via Docker DNS round-robin — no explicit replica list needed in the upstream block.

**Important constraint for CI:** The `ports:` entry must use `expose:` instead for scaled services to avoid host port conflicts. NGINX is the only service with a host port mapping.

---

## Data Contracts

### Docker Compose — Service Contract Changes

| Service | Before (Day 11) | After (Day 12) |
|---|---|---|
| `gateway` | `ports: ["5000:5000"]` | `expose: ["5000"]` |
| `userservice` | `ports: ["5001:5001"]` | `expose: ["5001"]` |
| `worker` | `ports: ["5002:5002"]` | `expose: ["5002"]` |
| `nginx` | *(does not exist)* | `ports: ["5000:80"]`, depends on all 3 services healthy |
| `postgres` | unchanged | unchanged |

### NGINX Upstream Contract

| Upstream Name | Target | Port | Health Check Endpoint |
|---|---|---|---|
| `backend_gateway` | `gateway` | `5000` | `/health/live` |
| `backend_userservice` | `userservice` | `5001` | `/health/live` |

### Environment Variables

| Variable | Value | Purpose |
|---|---|---|
| `NGINX_HOST` | `0.0.0.0` (default) | Listen address |
| `NGINX_PORT` | `80` (default, maps to host 5000) | Internal listen port |

No new environment variables for existing services — the backend addresses are resolved via Docker DNS (`gateway`, `userservice`, `worker` hostnames), which is automatic in Compose.

---

## Implementation Plan (Commit Units)

### Unit 1 — NGINX reverse proxy configuration + Docker Compose update

**Files:**
- `ops/nginx/nginx.conf` — new file with upstream blocks, location routing, proxy headers, health check config
- `docker-compose.yml` — add `nginx` service, change backend `ports:` to `expose:`, wire `depends_on` with health conditions

**Gate command:**
```bash
docker compose config -q && echo "VALID" || echo "INVALID"
```

**Commit message:**
```
feat(ops): add NGINX reverse proxy with upstream load balancing, update Docker Compose for multi-replica

Day 12 — unit 1 of 3 | Milestone M3.1
```

### Unit 2 — CI pipeline update for multi-replica validation

**Files:**
- `.github/workflows/ci.yml` — add job or step: `docker compose up --scale gateway=3 --scale userservice=3 --scale worker=3`, verify all 7 containers (3 gateway + 3 userservice + 3 worker + 1 nginx + 1 postgres = 11, but the services are 3+3+3+1+1=11 actually) are healthy, then run a smoke test through NGINX

**Gate command:**
```bash
# Validate the CI config parses
yamllint .github/workflows/ci.yml
```

**Commit message:**
```
ci(ops): add multi-replica startup verification and health-check smoke test

Day 12 — unit 2 of 3 | Milestone M3.1
```

### Unit 3 — Integration test: traffic distribution + replica kill resilience

**Files:**
- `tests/Kendo.Tests/Infrastructure/LoadBalancerTests.cs` — new test class with:
  1. `Request_RoutesToGateway_ThroughNginx` — curl NGINX port, verify response from Gateway
  2. `Request_RoutesToUserService_ThroughNginx` — `POST /api/users` through NGINX, verify 202 Accepted
  3. `Request_DistributesAcrossReplicas` — send N requests, verify ≥ 2 different upstream hostnames via response headers (requires `X-Upstream` or log inspection)
  4. `KillOneReplica_NoClientVisibleErrors` — docker stop one gateway replica, send requests, verify 0 non-5xx errors within health-check TTL
  5. `AllReplicasHealthy_AfterStartup` — `docker compose ps` shows all replicas healthy

**Note on test environment:** Tests 4–5 require Docker access and are marked as `[Category=Infrastructure]` — excluded from unit test runs, run separately in CI.

**Gate command:**
```bash
dotnet test tests/Kendo.Tests/Kendo.Tests.csproj --filter Category=Infrastructure --no-restore 2>&1
```

**Commit message:**
```
test(infra): add load balancer integration tests — traffic distribution, replica kill resilience

Day 12 — unit 3 of 3 | Milestone M3.1
```

---

## Success Checklist

- [ ] `docker compose config -q` succeeds with the new NGINX service and `expose:` declarations
- [ ] `docker compose up --scale gateway=3 --scale userservice=3 --scale worker=3` starts all 11 containers (3 gateway + 3 userservice + 3 worker + 1 nginx + 1 postgres) with all health checks passing
- [ ] `curl -s http://localhost:5000/health/live` returns 200 through NGINX (proxied to Gateway)
- [ ] `POST http://localhost:5000/api/users` through NGINX returns 202 with `Location` header (routed to UserService via proxy)
- [ ] Multiple sequential requests through NGINX distribute across different replica instances (verified by unique hostname responses or log correlation)
- [ ] `docker stop` on one gateway replica produces zero 5xx client errors within 15s (health-check TTL)
- [ ] All existing unit tests still pass (regression: 94+ existing tests)
- [ ] CI pipeline includes multi-replica startup step and passes
- [ ] No direct host-port access to backend services — all traffic passes through NGINX

---

## Resilience Mandate

### NGINX to Backend — Connection Timeouts

| Parameter | Value | Rationale |
|---|---|---|
| `proxy_connect_timeout` | 5s | Fast fail on unreachable replica — aligns with Polly circuit breaker threshold |
| `proxy_read_timeout` | 30s | Matches existing Gateway request timeout |
| `proxy_send_timeout` | 30s | Matches existing Gateway timeout |

All timeouts are conservative — lower than the Gateway's own timeout to ensure clients see an error from NGINX before the Gateway's own timeout fires.

### Health Check Integration

NGINX uses passive health checks by default (mark upstream unhealthy after `max_fails` failures within `fail_timeout`). For Docker Compose, passive checks are sufficient because Docker's own health checks already remove unhealthy containers from DNS rotation at the Docker level (Docker 20.10+).

**Configuration:**
```
upstream backend_gateway {
    server gateway:5000 max_fails=3 fail_timeout=10s;
}
```

If a replica is unreachable 3 times within 10s, NGINX marks it down and retries after `fail_timeout`. This prevents cascading failures — only the failing replica is removed from rotation.

### No Single Point of Failure

NGINX itself is a single point of failure in this local dev topology — this is **accepted** for Day 12. Production would deploy ≥ 2 NGINX instances with a cloud load balancer (Azure Application Gateway / AWS ALB) in front. This will be addressed in a future milestone (likely M3.6 runbook or a production-hardening day).

### Statelessness Enforcement

No sticky sessions (no `ip_hash`, no `sticky`). All services must remain stateless per the existing architecture decision log entry: *"All stateless APIs designed for replicas: 3; no in-process session state permitted."* NGINX round-robin enforces this — any replica must be able to handle any request.

### Fallback Behavior

If all replicas in an upstream are unhealthy:
- NGINX returns `502 Bad Gateway` with a standard error page
- The Gateway's own circuit breaker (from Phase 01) adds an additional resilience layer — if the Gateway can't reach UserService, it returns a RFC 7807 503 via the existing `ProblemDetailsMiddleware`
- Double-failover is intentional: NGINX 502 → client sees load balancer failure; Gateway 503 → client sees degraded but structured error. Both are acceptable for this milestone.

### N/A — No new API endpoints or data access patterns

This milestone does not introduce new API endpoints, database schemas, or message handlers. All existing resilience patterns (Polly Retry + Circuit Breaker, Rebus retry, idempotency, outbox) are unchanged.
