# Architecture Spec — Day 03

> **Milestone:** M1.3 — Resilience baseline  
> **Roadmap phase:** 01 — Core APIs & Synchronous Resilience  
> **Date:** 2026-06-13  
> **Architect:** Platform Architect (Orchestrator-delegated)

---

## Milestone Scope
P
- **Milestone:** M1.3 — Resilience baseline: all DB and external HTTP calls wrapped in Polly Retry + Circuit Breaker policies
- **Roadmap phase:** 01 — Core APIs & Synchronous Resilience
- **Components touched:**
  - `Polly Circuit Breaker` — Policy applied to all DB clients + external HTTP `HttpClient`
  - `Polly Retry` — Exponential backoff, transient-fault predicate, jitter
  - `User Service` — DbContext wrapped with Polly Retry + Circuit Breaker
  - `Gateway` — HttpClient registered with Polly Retry + Circuit Breaker (wired for future service-to-service calls)
  - `Worker` — HttpClient registered with Polly Retry + Circuit Breaker (wired for future external calls)
- **Explicitly out of scope:**
  - M1.4 — OpenTelemetry observability (traces, structured logs)
  - M1.5 — RFC 7807 Problem Details
  - M1.6 — Already complete (health checks)
  - Any Rebus / messaging infrastructure
  - Any domain entity design or new API endpoints

---

## Layer Changes

| Layer | Service | Change |
|-------|---------|--------|
| Application | UserService | Add Polly + Microsoft.Extensions.Http.Polly NuGet packages. Create `ResiliencePipelineFactory` service. Register Polly Retry + Circuit Breaker policies. Apply policies to `AppDbContext` via custom `ExecutionStrategy`. |
| Application | Gateway | Add `Microsoft.Extensions.Http.Polly` NuGet package. Register named `HttpClient` with Retry + Circuit Breaker policy handlers for future service-to-service calls. |
| Application | Worker | Add `Microsoft.Extensions.Http.Polly` NuGet package. Register named `HttpClient` with Retry + Circuit Breaker policy handlers for future external calls. |
| Configuration | All services | Resilience policy parameters externalized via `appsettings.json` (`Resilience` section). |
| Configuration | UserService | `docker-compose.yml` — pass `RESILIENCE__CIRCUITBREAKER__FAILURETHRESHOLD` and related env vars (optional; defaults in code). |

---

## Data Contracts

### Polly Policy Configuration

**Retry Policy:**
```json
{
  "Resilience": {
    "Retry": {
      "MaxRetries": 3,
      "BaseDelayMs": 100,
      "MaxDelayMs": 2000,
      "UseJitter": true
    },
    "CircuitBreaker": {
      "FailureThreshold": 3,
      "BreakDurationSeconds": 30,
      "SamplingDurationSeconds": 30
    }
  }
}
```

**Transient fault predicates:**
- DB: `NpgsqlException` with `IsTransient=true`, `TimeoutException`, `PostgresException` with transient SQLSTATE codes (08xxx, 53xxx, 57xxx)
- HTTP: `HttpRequestException`, `TaskCanceledException` (timeouts), 5xx status codes, 408 (Request Timeout)

### Resilience Policy Configuration Class

```csharp
namespace Kendo.Shared.Resilience;

public class ResilienceOptions
{
    public const string SectionName = "Resilience";

    public RetryOptions Retry { get; set; } = new();
    public CircuitBreakerOptions CircuitBreaker { get; set; } = new();
}

public class RetryOptions
{
    public int MaxRetries { get; set; } = 3;
    public int BaseDelayMs { get; set; } = 100;
    public int MaxDelayMs { get; set; } = 2000;
    public bool UseJitter { get; set; } = true;
}

public class CircuitBreakerOptions
{
    public int FailureThreshold { get; set; } = 3;
    public int BreakDurationSeconds { get; set; } = 30;
    public int SamplingDurationSeconds { get; set; } = 30;
}
```

### Resilience Pipeline Interface

```csharp
namespace Kendo.Shared.Resilience;

public interface IResiliencePipeline
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct = default);
    Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken ct = default);
}
```

**Implementation:** `PollyResiliencePipeline` wraps a Polly `ResiliencePipeline` built from `ResilienceOptions`.

**Registration (DI):**
```csharp
// In each service's Program.cs:
builder.Services.Configure<ResilienceOptions>(
    builder.Configuration.GetSection(ResilienceOptions.SectionName));
builder.Services.AddSingleton<IResiliencePipeline, PollyResiliencePipeline>();
```

---

## Implementation Plan (Commit Units)

> **Branch:** `feature/day-03-resilience-baseline` (off `develop`)

### Unit 1 — Add Polly NuGet packages to all services
- **Files:**
  - `src/UserService/Kendo.UserService.csproj` — add `Polly.Core` (vNext preview for .NET 10), `Microsoft.Extensions.Http.Polly`
  - `src/Gateway/Kendo.Gateway.csproj` — add `Microsoft.Extensions.Http.Polly`
  - `src/Worker/Kendo.Worker.csproj` — add `Microsoft.Extensions.Http.Polly`
  - `tests/Kendo.Tests/Kendo.Tests.csproj` — add `Polly.Core` (for test assertions), `Moq` (if not present), `Microsoft.Extensions.Http.Polly`
- **Gate command:** `dotnet build src/Kendo.slnx`
- **Commit message:** `chore(deps): add Polly.Core and Microsoft.Extensions.Http.Polly to all services`

### Unit 2 — Create shared resilience pipeline
- **Files:**
  - `src/Shared/Kendo.Shared.csproj` — new file (shared class library, referenced by all services)
  - `src/Shared/Resilience/ResilienceOptions.cs` — new file
  - `src/Shared/Resilience/IResiliencePipeline.cs` — new file
  - `src/Shared/Resilience/PollyResiliencePipeline.cs` — new file
  - `src/Shared/Resilience/ResilienceServiceCollectionExtensions.cs` — new file (DI extension methods)
- **Gate command:** `dotnet build src/Kendo.slnx`
- **Commit message:** `feat(resilience): create shared Polly resilience pipeline (Retry + Circuit Breaker)`

### Unit 3 — Apply resilience to UserService DbContext
- **Files:**
  - `src/UserService/Kendo.UserService.csproj` — add `ProjectReference` to `Kendo.Shared`
  - `src/UserService/Program.cs` — register `IResiliencePipeline`, configure resilience options
  - `src/UserService/Data/ResilientAppDbContext.cs` — new file (decorator wrapping `AppDbContext` with resilience pipeline)
  - `src/UserService/appsettings.json` — add `Resilience` section
- **Gate command:** `dotnet build src/Kendo.slnx && dotnet test tests/Kendo.Tests --filter "Category=Unit"`
- **Commit message:** `feat(data): wrap UserService DbContext with Polly Retry + Circuit Breaker`

### Unit 4 — Apply resilience to Gateway HttpClient
- **Files:**
  - `src/Gateway/Kendo.Gateway.csproj` — add `ProjectReference` to `Kendo.Shared`
  - `src/Gateway/Program.cs` — register named `HttpClient` with Polly Retry + Circuit Breaker, configure resilience options
  - `src/Gateway/appsettings.json` — add `Resilience` section
- **Gate command:** `dotnet build src/Kendo.slnx && dotnet test tests/Kendo.Tests --filter "Category=Unit"`
- **Commit message:** `feat(http): wire Gateway HttpClient with Polly Retry + Circuit Breaker`

### Unit 5 — Apply resilience to Worker HttpClient
- **Files:**
  - `src/Worker/Kendo.Worker.csproj` — add `ProjectReference` to `Kendo.Shared`
  - `src/Worker/Program.cs` — register named `HttpClient` with Polly Retry + Circuit Breaker, configure resilience options
  - `src/Worker/appsettings.json` — add `Resilience` section
- **Gate command:** `dotnet build src/Kendo.slnx && dotnet test tests/Kendo.Tests --filter "Category=Unit"`
- **Commit message:** `feat(http): wire Worker HttpClient with Polly Retry + Circuit Breaker`

### Unit 6 — Resilience tests (circuit breaker trip, reset, retry exhaustion)
- **Files:**
  - `tests/Kendo.Tests/Resilience/CircuitBreakerTests.cs` — new file: circuit breaker trips after threshold, resets after break duration, half-open state
  - `tests/Kendo.Tests/Resilience/RetryPolicyTests.cs` — new file: retries transient failures, exhausts retries, respects exponential backoff, jitter applies
  - `tests/Kendo.Tests/Resilience/ResiliencePipelineTests.cs` — new file: combined retry+CB pipeline, fallback behavior, DI registration resolves
- **Gate command:** `dotnet test tests/Kendo.Tests --filter "Category=Resilience"`
- **Commit message:** `test(resilience): add Polly circuit breaker and retry policy tests`

### Unit 7 — Integration tests and regression validation
- **Files:**
  - No new files — run full suite against Docker Compose
- **Gate command:** `dotnet test tests/Kendo.Tests && docker compose up -d --wait && docker compose ps --format json | jq '.[] | select(.Health != "healthy")' | wc -l | xargs -I{} sh -c '[ {} -eq 0 ]'`
- **Commit message:** `test(integration): validate all services healthy with resilience pipeline active`

---

## Success Checklist

| # | Criterion | Maps to |
|---|-----------|---------|
| 1 | `Polly.Core` and `Microsoft.Extensions.Http.Polly` NuGet packages installed in `UserService`, `Gateway`, `Worker`, and test project | Gate: `dotnet build` exits 0 |
| 2 | Shared resilience pipeline (`PollyResiliencePipeline`) builds Retry (3 attempts, exponential backoff with jitter) + Circuit Breaker (3 failures → 30s break) policies from config | Roadmap: "all DB and external HTTP calls wrapped in Polly Retry + Circuit Breaker policies" |
| 3 | `UserService` `AppDbContext` operations pass through Polly Retry + Circuit Breaker pipeline | Roadmap: "Circuit Breaker policy applied to all DB clients" |
| 4 | `Gateway` `HttpClient` registered via `IHttpClientFactory` with Polly Retry + Circuit Breaker policy handlers | Roadmap: "Circuit Breaker policy applied to external HTTP HttpClient" |
| 5 | `Worker` `HttpClient` registered via `IHttpClientFactory` with Polly Retry + Circuit Breaker policy handlers | Roadmap: "Circuit Breaker policy applied to external HTTP HttpClient" |
| 6 | Circuit breaker trips after 3 consecutive failures; returns a fallback response — not an unhandled exception | Roadmap AC: "Circuit breaker trips after the configured threshold; returns a fallback response — not an unhandled exception" |
| 7 | Retry policy retries transient failures up to 3 times with exponential backoff + jitter | Roadmap AC: "Retry policy retries transient DB failures up to N times with exponential backoff" |
| 8 | Circuit breaker resets after break duration; accepts requests in half-open state | Roadmap AC implicit |
| 9 | Retry exhaustion after N failed attempts correctly surfaces the last exception | Roadmap AC: "xUnit test suite covers ... retry exhaustion scenarios with simulated failures" |
| 10 | Existing 12/12 xUnit tests (health checks + data integration) still pass — no regressions | Regression guard |
| 11 | `docker compose up` starts all 4 services; all `/health/ready` probes pass | Integration validation |
| 12 | Resilience configuration read from `appsettings.json` `Resilience` section, overridable via env vars | Configuration externalization |

---

## Resilience Mandate

### DB Resilience (UserService)
| Property | Value |
|----------|-------|
| **Policy** | Retry + Circuit Breaker (combined pipeline) |
| **Retry count** | 3 |
| **Backoff** | Exponential: 100ms → 200ms → 400ms base, with 0-200ms jitter per attempt |
| **Transient predicates** | `NpgsqlException` (IsTransient), `TimeoutException`, `PostgresException` (SQLSTATE 08xxx/53xxx/57xxx) |
| **Circuit breaker threshold** | 3 consecutive failures in 30s sampling window |
| **Break duration** | 30 seconds |
| **Fallback** | Throw a typed `CircuitBrokenException` (not raw `BrokenCircuitException`) — consumed by calling code or future RFC 7807 middleware |

### HTTP Resilience (Gateway + Worker)
| Property | Value |
|----------|-------|
| **Policy** | Retry + Circuit Breaker (combined, per named `HttpClient`) |
| **Retry count** | 3 |
| **Backoff** | Exponential: 100ms → 200ms → 400ms base, with 0-200ms jitter per attempt |
| **Transient predicates** | `HttpRequestException`, `TaskCanceledException` (timeout), HTTP 5xx, HTTP 408 |
| **Circuit breaker threshold** | 3 consecutive failures in 30s sampling window |
| **Break duration** | 30 seconds |
| **Fallback** | Return HTTP 503 with `Retry-After: 30` header (pre-RFC 7807 — full error schema deferred to M1.5) |

### Not Applicable
- **Async messaging:** N/A — Rebus/Azure Service Bus deferred to Phase 02.
- **OpenTelemetry correlation:** N/A — deferred to M1.4. Retry traces will be logged as structured `ILogger` messages in M1.3; trace correlation arrives next milestone.

---

## Dependency Check

| Resource | Required? | Status |
|----------|-----------|--------|
| PostgreSQL | Yes — UserService DbContext targets it | ✅ Provisioned (Day 02, Docker Compose) |
| AppDbContext | Yes — wrapped with Polly pipeline | ✅ Exists in UserService (Day 02) |
| Gateway | Yes — HttpClient registered with Polly | ✅ Exists (Day 01) |
| Worker | Yes — HttpClient registered with Polly | ✅ Exists (Day 01) |
| Shared project | Yes — new `Kendo.Shared` class library for resilience pipeline | ✨ Created in Unit 2 |
| Polly NuGet | Yes — `Polly.Core`, `Microsoft.Extensions.Http.Polly` | ✨ Added in Unit 1 |
| RabbitMQ | No | Deferred to Phase 02 |
| Redis | No | Deferred to Phase 03 |
| OpenTelemetry | No | Deferred to M1.4 |

---

## Open Questions / Clarifications

- **Polly.Core vs Polly:** .NET 10 may have a stable `Polly.Core` release. The spec targets `Polly.Core` (the newer, pipeline-based API). If `Polly.Core` is not yet stable for .NET 10, fall back to `Polly` (v8.x) with `ResiliencePipelineBuilder`. Gate check at Unit 1: `dotnet restore` must succeed.
- **Shared project placement:** Proposing `src/Shared/Kendo.Shared.csproj` as a class library referenced by all services. This avoids duplication of `ResilienceOptions`, `IResiliencePipeline`, and `PollyResiliencePipeline` across three services. Confirm this pattern is acceptable — it also serves as the foundation for future shared concerns (RFC 7807 middleware, OpenTelemetry config).
- **Fallback vs exception propagation:** In M1.3, the circuit breaker fallback throws a typed `CircuitBrokenException`. In M1.5 (RFC 7807), this will be caught by exception-handling middleware and mapped to a `503 Service Unavailable` Problem Details response. This is intentional layering.
- **No existing HTTP calls:** Gateway and Worker currently have zero `HttpClient` usage. The spec registers the resilient `HttpClient` now (units 4-5) so it's available when service-to-service calls are added in future milestones. The tests in Unit 6 validate the policy behavior directly — not via live HTTP calls.
