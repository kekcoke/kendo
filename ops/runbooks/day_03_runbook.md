# Ops Runbook — Day 3

> **Milestone:** M1.3 — Resilience baseline (Polly Retry + Circuit Breaker)  
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

## Resilience Policy Configuration

| Policy | Property | Value | Config Path |
|--------|----------|-------|-------------|
| **Retry** | Max Retries | 3 | `Resilience:Retry:MaxRetries` |
| **Retry** | Base Delay | 100ms | `Resilience:Retry:BaseDelayMs` |
| **Retry** | Max Delay | 2000ms | `Resilience:Retry:MaxDelayMs` |
| **Retry** | Backoff | Exponential | Built-in |
| **Retry** | Jitter | Enabled (0-200ms) | `Resilience:Retry:UseJitter` |
| **Circuit Breaker** | Failure Threshold | 3 consecutive | `Resilience:CircuitBreaker:FailureThreshold` |
| **Circuit Breaker** | Break Duration | 30 seconds | `Resilience:CircuitBreaker:BreakDurationSeconds` |
| **Circuit Breaker** | Sampling Window | 30 seconds | `Resilience:CircuitBreaker:SamplingDurationSeconds` |

**Override via environment variables:**
```bash
Resilience__Retry__MaxRetries=5
Resilience__CircuitBreaker__FailureThreshold=5
Resilience__CircuitBreaker__BreakDurationSeconds=60
```

---

## Services with Active Resilience

| Service | Pipeline Type | Wraps | Registration |
|---------|--------------|-------|-------------|
| UserService | Retry + Circuit Breaker | `AppDbContext` (via `ResilientAppDbContext` decorator) | `services.AddKendoResilience(config)` in `Program.cs` |
| Gateway | Retry + Circuit Breaker | Named `HttpClient` ("default") via `ResilienceDelegatingHandler` | `AddHttpClient("default").AddHttpMessageHandler<ResilienceDelegatingHandler>()` |
| Worker | Retry + Circuit Breaker | Named `HttpClient` ("default") via `ResilienceDelegatingHandler` | `AddHttpClient("default").AddHttpMessageHandler<ResilienceDelegatingHandler>()` |

---

## Deployment Playbook

### Prerequisites
- Docker Desktop (or Docker Engine + Compose v2) installed
- .NET 10 SDK installed
- Ports 5000, 5001, 5002, 5432 available
- Local PostgreSQL stopped if using port 5432

### Deploy Locally

```bash
# 1. Checkout feature branch
git checkout feature/day-03-resilience-baseline

# 2. Build images
docker compose build

# 3. Start all services
docker compose up -d

# 4. Wait for healthy (all four should show healthy)
docker compose ps

# 5. Verify health endpoints
curl http://localhost:5000/health/live
curl http://localhost:5001/health/live
curl http://localhost:5002/health/live

# 6. Run full test suite including resilience tests
dotnet test src/Kendo.slnx
```

### Deploy via CI
Push to `feature/day-03-resilience-baseline` or open PR against `develop`.
CI pipeline runs: build → unit tests → data integration tests → resilience tests → docker compose → health verification.

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

### Resilience Configuration Rollback
If circuit breaker is too aggressive:
```bash
# Set higher thresholds via env vars in docker-compose.yml:
#   Resilience__CircuitBreaker__FailureThreshold: 10
#   Resilience__CircuitBreaker__BreakDurationSeconds: 10

# Or revert to defaults by removing the Resilience section from appsettings.json
```

---

## Incident Triage

| Symptom | Check | Recovery |
|---------|-------|----------|
| Circuit breaker open unexpectedly | Check `Resilience:CircuitBreaker:FailureThreshold` in appsettings | Increase threshold or decrease transient false-positives |
| DB operations failing after retries | `docker compose logs userservice` | Check PostgreSQL connectivity, verify connection string |
| HTTP calls failing with `BrokenCircuitException` | Check downstream service health | Restart downstream service; CB auto-resets after break duration (30s) |
| Retries causing excessive latency | Check `Resilience:Retry:MaxRetries` | Reduce to 1-2 retries for latency-sensitive endpoints |
| All services unhealthy after deploy | `docker compose ps` | Check `depends_on` ordering; PostgreSQL must be healthy before UserService starts |
| NuGet restore fails | `dotnet restore --verbosity detailed` | Check Polly.Core 8.7.0 and Microsoft.Extensions.Http.Polly 10.0.9 availability |

### Circuit Breaker State Check (Manual)
Polly v8 does not expose circuit state directly through the `ResiliencePipeline` API. To verify circuit breaker behavior:
```bash
# Run resilience tests (these validate CB trip/reset programmatically)
dotnet test src/Kendo.slnx --filter "Category=Resilience"
```

---

## Shared Library Reference

| Project | Path | Purpose |
|---------|------|---------|
| `Kendo.Shared` | `src/Shared/Kendo.Shared.csproj` | Shared resilience pipeline, options, DI extensions |
| `ResilienceOptions` | `src/Shared/Resilience/ResilienceOptions.cs` | Policy configuration model |
| `IResiliencePipeline` | `src/Shared/Resilience/IResiliencePipeline.cs` | Pipeline interface |
| `PollyResiliencePipeline` | `src/Shared/Resilience/PollyResiliencePipeline.cs` | Polly v8 implementation |
| `ResilienceDelegatingHandler` | `src/Shared/Http/ResilienceDelegatingHandler.cs` | HTTP message handler wrapper |
| `ResilientAppDbContext` | `src/UserService/Data/ResilientAppDbContext.cs` | DB context decorator |
