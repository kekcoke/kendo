# Day 05 — Architecture Specification

## Milestone Scope

- **Milestone:** M1.5 — API contract: standardized RFC 7807 Problem Details on all 4xx/5xx responses across every service
- **Roadmap phase:** 01 — Core APIs & Synchronous Resilience
- **Components touched:**
  - `Kendo.Shared` — new `ErrorHandling/` module (ProblemDetails middleware + extension)
  - `Gateway` — wire middleware into pipeline
  - `UserService` — wire middleware into pipeline
  - `Worker` — wire middleware into pipeline
- **Explicitly out of scope:**
  - M1.6 — Health check endpoints already ✅
  - Phase 02 (M2.1–M2.6) — Async decoupling, Rebus, Azure Service Bus
  - Any existing endpoint business logic modification or new business endpoints

## Carry-Forward Items Resolved

| Item | Status |
|---|---|
| Define global RFC 7807 error schema for the API layer *(carried from Day 00)* | ✅ In scope — resolved as `KendoProblemDetails` schema |

## Layer Changes

### Kendo.Shared (`src/Shared/`)
New `ErrorHandling/` directory:
- `KendoProblemDetails.cs` — RFC 7807 + trace-ID extension subclass
- `ProblemDetailsMiddleware.cs` — catches unhandled exceptions → RFC 7807 JSON response
- `ErrorHandlingServiceCollectionExtensions.cs` — `AddKendoErrorHandling()` / `UseKendoErrorHandling()` extension methods

### Gateway, UserService, Worker
Each `Program.cs` receives a one-line addition to the middleware pipeline.

No other files in these projects change.

## Data Contracts

### RFC 7807 Problem Details Schema (all services, all endpoints)

RFC 7807, aka "Problem Details for HTTP APIs" ([RFC 9457](https://www.rfc-editor.org/rfc/rfc9457)), defines a standard machine-readable error response body. All endpoints across all services must return this shape on 4xx/5xx responses.

All errors flow through one consistent middleware layer located in `Kendo.Shared`.

**Standard Problem + Trace Extension:**

```json
{
  "type": "https://httpstatuses.com/{statusCode}",
  "title": "Standard HTTP status text",
  "status": 400,
  "detail": "Human-readable description of the specific error",
  "instance": "/api/resource",
  "traceId": "00-0ab5c8f1e3d74a2b9c6f7d8e9a0b1c2d-3e4f5a6b7c8d9e0f-01"
}
```

**Validation Errors (ModelState invalid):**

```json
{
  "type": "https://httpstatuses.com/400",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "detail": "A human-readable summary of the validation failure.",
  "instance": "/api/resource",
  "traceId": "00-0ab5c8f1e3d74a2b9c6f7d8e9a0b1c2d-3e4f5a6b7c8d9e0f-01",
  "errors": {
    "PropertyName": ["'PropertyName' must not be empty."]
  }
}
```

### Exception → Status Code Mapping

| Exception Type | HTTP Status | `title` |
|---|---|---|
| `ArgumentException` / `ArgumentNullException` | 400 | Bad Request |
| `KeyNotFoundException` | 404 | Not Found |
| `OperationCanceledException` / `TaskCanceledException` | 503 | Service Unavailable |
| `HttpRequestException` (or other transient) | 503 | Service Unavailable |
| `ValidationException` (FluentValidation) | 400 | One or more validation errors occurred. |
| All other unhandled exceptions | 500 | An error occurred while processing your request. |

Health endpoints (`/health/live`, `/health/ready`) are **exempt** from RFC 7807 — they return `text/plain` as they always have.

### `KendoProblemDetails` C# Class Shape

```csharp
namespace Kendo.Shared.ErrorHandling;

// Extends ASP.NET Core's built-in ProblemDetails with traceId
public class KendoProblemDetails
{
    public string Type { get; set; } = string.Empty;        // "https://httpstatuses.com/{statusCode}"
    public string Title { get; set; } = string.Empty;       // e.g. "Bad Request"
    public int Status { get; set; }                         // HTTP status code
    public string Detail { get; set; } = string.Empty;      // human-readable detail
    public string Instance { get; set; } = string.Empty;    // request path
    public string TraceId { get; set; } = string.Empty;     // OpenTelemetry trace ID
    public IDictionary<string, string[]>? Errors { get; set; } // validation errors (optional)
}
```

### Middleware Behavior

The `ProblemDetailsMiddleware` sits after `UseRouting()` but before `UseAuthorization()` / `MapControllers()`. For every request:

1. **If response is 4xx/5xx after controller execution** (status code already set) → replace body with ProblemDetails JSON.
2. **If an unhandled exception is thrown** → catch, log with structured OTel trace context, set status code + ProblemDetails body.
3. **Health endpoints** (`/health/live`, `/health/ready`) → skip middleware entirely.

## Implementation Plan (Commit Units)

### Unit 1 — ProblemDetails model, middleware, and extension methods in Kendo.Shared

- **Files:**
  - `src/Shared/ErrorHandling/KendoProblemDetails.cs` — ProblemDetails DTO with traceId
  - `src/Shared/ErrorHandling/ProblemDetailsMiddleware.cs` — middleware class
  - `src/Shared/ErrorHandling/ErrorHandlingServiceCollectionExtensions.cs` — `AddKendoErrorHandling()` and `UseKendoErrorHandling()` extension methods
- **Gate command:** `dotnet build src/Shared/Kendo.Shared.csproj --no-restore 2>&1`
- **Commit message:**
  ```
  feat(shared): add RFC 7807 ProblemDetails middleware to Kendo.Shared

  Day 05 — unit 1 of 3 | Milestone M1.5
  ```

### Unit 2 — Wire middleware into all 3 services + configure validation error handling

- **Files:**
  - `src/Gateway/Program.cs` — add `app.UseKendoErrorHandling();`
  - `src/UserService/Program.cs` — add `app.UseKendoErrorHandling();`
  - `src/Worker/Program.cs` — add `app.UseKendoErrorHandling();`
  - Add `builder.Services.Configure<ApiBehaviorOptions>(o => o.SuppressModelStateInvalidFilter = true);` to all 3 services and handle validation in the middleware
  - Add `System.Text.Json` serialization config (camelCase, enums as strings) to all 3 services
- **Gate command:** `dotnet build --no-restore 2>&1`
- **Commit message:**
  ```
  feat(services): wire ProblemDetails middleware into Gateway, UserService, Worker

  Day 05 — unit 2 of 3 | Milestone M1.5
  ```

### Unit 3 — Unit and integration tests for ProblemDetails middleware

- **Files:**
  - `tests/Kendo.Tests/ErrorHandling/ProblemDetailsMiddlewareTests.cs` — unit tests for middleware behavior
- **Gate command:** `dotnet test tests/Kendo.Tests/Kendo.Tests.csproj --no-restore 2>&1`
- **Commit message:**
  ```
  test(shared): add ProblemDetails middleware tests

  Day 05 — unit 3 of 3 | Milestone M1.5
  Coverage: reported %
  Lint: clean
  ```

## Success Checklist

- [ ] All 3 services return RFC 7807 `application/problem+json` body on 4xx/5xx responses (verified by test)
- [ ] Unhandled `KeyNotFoundException` → 404 ProblemDetails with correct type/title/status/detail/traceId (verified by test)
- [ ] Unhandled `ArgumentException` → 400 ProblemDetails (verified by test)
- [ ] Unhandled `OperationCanceledException` → 503 ProblemDetails (verified by test)
- [ ] Unhandled generic `Exception` → 500 ProblemDetails (verified by test)
- [ ] No controller action returns a raw `Exception` message to the client — all unhandled exceptions are caught by middleware (verified by test)
- [ ] OpenTelemetry trace ID (`Activity.Current.TraceId`) appears in every ProblemDetails response's `traceId` field (verified by test)
- [ ] All existing health endpoint tests continue to pass (regression — verify `text/plain` responses unchanged)
- [ ] `docker compose up` still starts all services; all `/health/ready` probes pass

## Resilience Mandate

**N/A — no external dependencies this milestone.**

The ProblemDetails middleware is pure middleware. It has no database, HTTP, or message broker dependencies. It catches exceptions and returns structured error responses. Circuit breakers, retries, and async boundaries are not applicable because the middleware operates within a single synchronous request/response pipeline.

The middleware integrates with OpenTelemetry by reading `Activity.Current.TraceId` for the `traceId` field, but does not itself open any spans — it enriches the existing trace context.
