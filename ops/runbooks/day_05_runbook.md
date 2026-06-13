# Ops Runbook — Day 5

> **Milestone:** M1.5 — API contract: standardized RFC 7807 Problem Details on all 4xx/5xx responses
> **Services:** Gateway (port 5000), UserService (port 5001), Worker (port 5002)
> **Date:** 2026-06-13

---

## Health & Observability Contract

| Service | Liveness Probe | Readiness Probe | Expected Response |
|---------|---------------|-----------------|-------------------|
| Gateway | `GET :5000/health/live` | `GET :5000/health/ready` | `200 OK` — `Healthy` / `Ready` |
| UserService | `GET :5001/health/live` | `GET :5001/health/ready` | `200 OK` — `Healthy` / `Ready` |
| Worker | `GET :5002/health/live` | `GET :5002/health/ready` | `200 OK` — `Healthy` / `Ready` |

**Error response contract:** All non-health endpoints return `application/problem+json` bodies on 4xx/5xx responses.

**Trace correlation:** Every ProblemDetails response includes the OpenTelemetry `traceId` field, enabling end-to-end error tracing.

---

## What Changed This Day

| Change | Impact |
|---|---|
| `KendoProblemDetails` DTO added to `Kendo.Shared` | RFC 7807 standard error shape across all services |
| `ProblemDetailsMiddleware` in `Kendo.Shared` | Catches unhandled exceptions → structured RFC 7807 JSON; passes healthy endpoints through unchanged |
| `ErrorHandlingServiceCollectionExtensions` (Add/Use) | One-liner activation per service — minimal friction |
| Middleware wired into Gateway, UserService, Worker | Consistent error contract across the entire platform |
| 10 new unit tests for ProblemDetails middleware | Coverage for health skip, 400/404/499/500/503, trace-ID, content-type |

### Exception → HTTP Status Mapping

| Exception | Status | Title |
|---|---|---|
| `ArgumentException` | 400 | Bad Request |
| `KeyNotFoundException` | 404 | Not Found |
| `OperationCanceledException` (client abort) | 499 | — (no body) |
| `OperationCanceledException` / `TaskCanceledException` | 503 | Service Unavailable |
| `HttpRequestException` | 503 | Service Unavailable |
| All others | 500 | An error occurred while processing your request. |

---

## Verification Playbook

### 1. Verify Health Endpoints Still Work (Regression)

```bash
# Start services
docker compose up -d

# Health endpoints must return text/plain — NOT application/problem+json
curl -sS http://localhost:5000/health/live
# Expected: "Healthy" (plain text)

curl -sS http://localhost:5001/health/ready
# Expected: "Ready" (plain text)
```

### 2. Verify 404 ProblemDetails Response

```bash
# Any service — request a non-existent resource
curl -sS -w "\nHTTP %{http_code}\n" http://localhost:5000/api/nonexistent

# Expected: 404 with RFC 7807 JSON body
# {
#   "type": "https://httpstatuses.com/404",
#   "title": "Not Found",
#   "status": 404,
#   "detail": "...",
#   "instance": "/api/nonexistent",
#   "traceId": "00-..."
# }
```

### 3. Verify Trace ID in Error Responses

```bash
# Extract traceId from any error response
curl -sS http://localhost:5001/api/nonexistent | python3 -m json.tool | grep traceId

# Expected: non-empty hex trace ID (e.g. "00-0ab5c8f1e3d74a2b...")
```

### 4. Verify Content-Type Header

```bash
curl -sv http://localhost:5000/api/nonexistent 2>&1 | grep -i content-type

# Expected: "content-type: application/problem+json; charset=utf-8"
```

### 5. Verify CI Pipeline

```bash
# All 39 tests (29 existing + 10 new) must pass:
# - Unit: health check + ProblemDetails middleware tests (Category=Unit)
# - Data: existing integration tests (Category=Data)
# - Resilience: existing circuit breaker + retry tests (Category=Resilience)
# - Docker Compose: health endpoint accessibility
```

---

## Rollback Plan

### Rollback Strategy
1. **If PR not merged:** Close PR. Branch is `feature/day-05-problem-details-middleware`.
2. **If merged:** `git revert` the merge commit on `develop`.
3. **Verify:** Health endpoints return `200 OK` (unchanged behavior).

### Rollback Scope
| Component | Rollback Action | Impact |
|---|---|---|
| `KendoProblemDetails` + middleware | Revert `src/Shared/ErrorHandling/` | No structured error responses — unhandled exceptions return raw ASP.NET error pages |
| Service `Program.cs` changes | Revert `UseKendoErrorHandling()` lines | No middleware registered |
| JSON serialization config | Revert `AddJsonOptions` + `SuppressModelStateInvalidFilter` in all 3 `Program.cs` | Falls back to default ASP.NET Core JSON config |
| Tests | Revert `tests/Kendo.Tests/ErrorHandling/` | 10 tests removed; count drops from 39 to 29 |

**Note:** All changes in this milestone are additive middleware. No business logic changed. Rollback is safe at any point — health endpoints return `text/plain` unaffected.

---

## Monitoring Hooks

- **ProblemDetails logging:** Middleware logs unhandled exceptions at `LogLevel.Error` with structured context (method, path, exception)
- **Trace ID propagation:** Every error response carries the request's OpenTelemetry trace ID — link errors to trace spans in the console exporter
- **499 Client Closed:** Client disconnections return 499 without error body — avoids noise in error dashboards from cancelled requests

---

## Known Issues / Caveats

- **FluentValidation support:** The current `ProblemDetailsMiddleware` maps `ValidationException` to 400, but FluentValidation NuGet packages are not yet in the project. If FluentValidation is added later, the middleware will handle it correctly via the `ValidationException` switch case.
- **SuppressModelStateInvalidFilter:** We suppress ASP.NET Core's built-in `InvalidModelStateResponseFactory` (which returns a bare `ProblemDetails`) to let the middleware handle all error serialization uniformly. This is intentional — the middleware produces the same RFC 7807 shape with `traceId` extension.
- **Health endpoint exemption:** The middleware skips `/health/live` and `/health/ready` entirely. These always return `text/plain` as they did before.
