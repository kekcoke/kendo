# Day 14 Runbook — Rate Limiting & Load Shedding (M3.4)
> **Phase 4 artifact.** Created by DevOps/SRE agent.

## Overview
Introduced .NET rate limiting middleware with fixed-window policy and a concurrency-based load shedder at the Gateway service. Both are bypassed for health endpoints to prevent cascading health-check failures.

## Configuration

### Rate limiting (`RateLimiting__FixedWindow`)

| Parameter | Default | Description |
|---|---|---|
| `PermitLimit` | 100 | Max requests per time window per client IP |
| `WindowSeconds` | 60 | Time window in seconds |
| `QueueLimit` | 10 | Max queued requests when limit exceeded (0 = reject immediately) |

### Load shedding (`RateLimiting__Concurrency`)

| Parameter | Default | Description |
|---|---|---|
| `MaxConcurrency` | 50 | Max concurrent in-flight requests |
| `QueueLimit` | 5 | Max queued requests before 503 (0 = reject immediately) |

## Middleware Execution Order (Gateway)

```
Request → LoadSheddingMiddleware → RateLimitingMiddleware → ProblemDetailsMiddleware → Controllers
         (503 if saturated)        (429 if exceeded)        (RFC 7807 body)            (business logic)
```

## Failure Scenarios & Recovery

### 1. Rate limit exceeded (429)
- **Symptom:** Client receives `429 Too Many Requests` with `Retry-After` header and RFC 7807 body
- **Cause:** Client exceeded `PermitLimit` requests within `WindowSeconds`
- **Resolution:** Client waits for `Retry-After` seconds and retries
- **Monitoring:** Logged at `Warning` level with method, path, retry-after value

### 2. Load shedding triggered (503)
- **Symptom:** Client receives `503 Service Unavailable` with RFC 7807 body (no `Retry-After`)
- **Cause:** Max concurrent in-flight requests (`MaxConcurrency`) exceeded
- **Resolution:** Reduce concurrent requests or scale Gateway replicas horizontally
- **Monitoring:** Logged at `Warning` level with queued count, method, path

### 3. Health check failure cascade
- **Both middleware bypass** `/health/live` and `/health/ready` — health probes always pass
- If a health check fails, investigate the application layer, not rate limiting

## Verification Commands

```bash
# Test rate limiting (expect 429 after threshold)
for i in $(seq 1 200); do
  curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5000/api/users &>/dev/null
done | sort | uniq -c

# Test load shedding (expect 503 under concurrent requests)
# Requires concurrency limit set to a low value in appsettings

# Verify health bypass (always 200)
curl -s -o /dev/null -w "%{http_code}" http://localhost:5000/health/live
curl -s -o /dev/null -w "%{http_code}" http://localhost:5000/health/ready
```

## Rollback Plan
1. Revert the feature branch merge from `develop`
2. Remove `app.UseKendoLoadShedding()` and `app.UseKendoRateLimiter()` from `Program.cs`
3. Remove `builder.Services.AddKendoRateLimiting()` from `Program.cs`
4. Remove `RateLimiting` config section from `appsettings.json`
5. Delete `src/Shared/RateLimiting/` directory
6. Delete `tests/Kendo.Tests/RateLimiting/` directory
