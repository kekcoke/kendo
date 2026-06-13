# Architecture Spec — Day 04

> **Milestone:** M1.4 — Observability foundation  
> **Roadmap phase:** 01 — Core APIs & Synchronous Resilience  
> **Date:** 2026-06-13  
> **Architect:** Platform Architect (Orchestrator-delegated)

---

## Milestone Scope

- **Milestone:** M1.4 — Observability foundation: OpenTelemetry traces and structured logs flowing to console/collector; trace IDs in all log lines
- **Roadmap phase:** 01 — Core APIs & Synchronous Resilience
- **Components touched:**
  - `OpenTelemetry` — Traces + structured logs, console exporter, trace-ID in all logs (all three services: Gateway, UserService, Worker)
  - `Shared Library` — OpenTelemetry configuration extension method registered once, used by all services
  - `Polly Resilience` — Existing `OnRetry`/`OnOpened`/`OnClosed`/`OnHalfOpened` callbacks upgraded from placeholder `ValueTask.CompletedTask` to structured `ILogger` calls with trace correlation
  - `CI Pipeline` — PostgreSQL service container added to `build-and-test` job (carry-forward resolution)
- **Explicitly out of scope:**
  - M1.5 — RFC 7807 Problem Details error schema (carry-forward, deferred)
  - M1.6 — Health check enhancements (already complete)
  - Phase 02 M2.1-M2.6 — Async decoupling, Rebus, Azure Service Bus (not yet started)
  - Phase 03 M3.1-M3.6 — High availability, chaos testing (future phase)

---

## Layer Changes

| Project | Change |
|---|---|
| `src/Shared/Kendo.Shared.csproj` | Add `OpenTelemetry`, `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Exporter.Console` NuGet packages |
| `src/Shared/` — new file `Observability/ObservabilityServiceCollectionExtensions.cs` | Extension method `AddKendoObservability()` — registers OpenTelemetry tracing with `AddConsoleExporter()`, sets `AddSource("Kendo.*")`, configures enrichment with `HttpRequest`/`HttpResponse` data |
| `src/Shared/Resilience/PollyResiliencePipeline.cs` | Inject `ILogger<PollyResiliencePipeline>` — replace placeholder `OnRetry`/`OnOpened`/`OnClosed`/`OnHalfOpened` callbacks with `_logger.LogWarning(...)` including trace ID context |
| `src/Gateway/Program.cs` | Add `builder.Services.AddKendoObservability()` call |
| `src/UserService/Program.cs` | Add `builder.Services.AddKendoObservability()` call |
| `src/Worker/Program.cs` | Add `builder.Services.AddKendoObservability()` call |
| `.github/workflows/ci.yml` | Add `services` block with PostgreSQL 16 container in `build-and-test` job |
| `tests/Kendo.Tests/` — new file `Observability/ObservabilityTests.cs` | Verify trace IDs appear in log output; verify console exporter writes spans |

---

## Data Contracts

### OpenTelemetry Configuration

| Setting | Value |
|---|---|
| **Tracing exporter** | Console exporter (OTLP collector deferred — Phase 02 or later) |
| **Activity sources** | `Kendo.Gateway`, `Kendo.UserService`, `Kendo.Worker` (auto via `AddAspNetCoreInstrumentation`) |
| **Logging enrichment** | `AddOpenTelemetryLogging` with `IncludeFormattedMessage = true`, `IncludeScopes = true` |
| **Auto-instrumentation** | `OpenTelemetry.Instrumentation.AspNetCore` — captures HTTP requests/responses, enriches spans with route, method, status code, duration |
| **Sampling** | `AlwaysOnSampler` (development); override via env var `OTEL_TRACES_SAMPLER` for production |
| **Resource attributes** | `service.name` set per service: `kendo-gateway`, `kendo-userservice`, `kendo-worker` |
| **Trace ID in logs** | ASP.NET Core auto-correlation via `Activity.Current.TraceId` in `ILogger` scopes (no custom middleware needed when `AddOpenTelemetryLogging` is used) |

### CI PostgreSQL Service Container

```yaml
services:
  postgres:
    image: postgres:16-alpine
    env:
      POSTGRES_DB: kendo_users
      POSTGRES_USER: kendo
      POSTGRES_PASSWORD: kendo_test
    ports:
      - 5432:5432
    options: >-
      --health-cmd pg_isready
      --health-interval 5s
      --health-timeout 3s
      --health-retries 5
```

Corresponding env var for `build-and-test` job steps:

```yaml
env:
  ConnectionStrings__DefaultConnection: Host=localhost;Port=5432;Database=kendo_users;Username=kendo;Password=kendo_test
```

---

## Implementation Plan (Commit Units)

### Unit 1 — Add OpenTelemetry packages to Kendo.Shared and create observability extension

- **Files:**
  - `src/Shared/Kendo.Shared.csproj` — add OTel package references
  - `src/Shared/Observability/ObservabilityServiceCollectionExtensions.cs` — new file: `AddKendoObservability()` extension method
- **Gate command:** `dotnet build src/Kendo.slnx --no-restore && dotnet test src/Kendo.slnx --filter "Category=Unit" --no-build`
- **Commit message:** `feat(shared): add OpenTelemetry tracing configuration extension method`

### Unit 2 — Wire OpenTelemetry into all three services

- **Files:**
  - `src/Gateway/Program.cs` — add `builder.Services.AddKendoObservability()`
  - `src/UserService/Program.cs` — add `builder.Services.AddKendoObservability()`
  - `src/Worker/Program.cs` — add `builder.Services.AddKendoObservability()`
- **Gate command:** `dotnet build src/Kendo.slnx && dotnet test src/Kendo.slnx --filter "Category=Unit"`
- **Commit message:** `feat(services): wire OpenTelemetry tracing into Gateway, UserService, and Worker`

### Unit 3 — Upgrade resilience callbacks with structured logging

- **Files:**
  - `src/Shared/Resilience/PollyResiliencePipeline.cs` — inject `ILogger<PollyResiliencePipeline>`; replace all four placeholder callbacks with `_logger.LogWarning(...)` calls that include trace ID for correlation
- **Gate command:** `dotnet build src/Kendo.slnx && dotnet test src/Kendo.slnx --filter "Category=Unit"`
- **Commit message:** `feat(resilience): add structured logging to Polly retry and circuit-breaker callbacks`

### Unit 4 — Add PostgreSQL service container to CI workflow

- **Files:**
  - `.github/workflows/ci.yml` — add `services` block with Postgres 16-alpine under `build-and-test` job; add `ConnectionStrings__DefaultConnection` env var to `Run data integration tests` step
- **Gate command:** `dotnet build src/Kendo.slnx && dotnet test src/Kendo.slnx --filter "Category=Data"`
- **Commit message:** `fix(ci): add PostgreSQL service container to build-and-test job for data integration tests`

### Unit 5 — Add OpenTelemetry observability tests

- **Files:**
  - `tests/Kendo.Tests/Observability/ObservabilityTests.cs` — new file: tests that verify trace IDs are present in log output, console exporter writes spans, and resilience callbacks emit structured logs with trace correlation
- **Gate command:** `dotnet test src/Kendo.slnx --filter "Category=Observability"`
- **Commit message:** `test(observability): add tests for trace ID correlation and structured logging`

---

## Success Checklist

- [ ] OpenTelemetry NuGet packages installed (`OpenTelemetry`, `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Exporter.Console`, `OpenTelemetry.Instrumentation.AspNetCore`)
- [ ] `AddKendoObservability()` extension method exists in `Kendo.Shared` and registers console exporter plus ASP.NET Core instrumentation
- [ ] All three services (Gateway, UserService, Worker) call `builder.Services.AddKendoObservability()` in `Program.cs`
- [ ] Trace IDs appear in all `ILogger` log lines for a given request (ASP.NET Core auto-correlation via `Activity.Current.TraceId`)
- [ ] Polly resilience callbacks (`OnRetry`, `OnOpened`, `OnClosed`, `OnHalfOpened`) emit structured `LogWarning`/`LogInformation` messages with trace context
- [ ] CI `build-and-test` job has a `services` block with PostgreSQL 16-alpine container and health check
- [ ] `ConnectionStrings__DefaultConnection` env var is set for `Run data integration tests` step in CI
- [ ] All existing tests (Unit, Data, Resilience) still pass after changes
- [ ] New observability tests pass: verify trace ID presence, span export, and callback logging

---

## Resilience Mandate

### OpenTelemetry Correlation (New)

| Resource | Configuration |
|---|---|
| **Tracing provider** | `TracerProviderBuilder` via `AddKendoObservability()` |
| **Instrumentation** | `AddAspNetCoreInstrumentation()` — auto-captures HTTP request/response spans |
| **Console exporter** | `AddConsoleExporter()` — writes spans to stdout (OTLP collector for production in Phase 02+) |
| **ActivitySource** | `Kendo.Shared` — shared source name for manual instrumentation in resilience pipeline |
| **Resource** | `service.name` set per service via environment detection or explicit config |

### Polly Callback Logging (Upgraded)

| Callback | Log level | Message pattern | Trace correlation |
|---|---|---|---|
| `OnRetry` | `Warning` | `"Retry attempt {Attempt}/{MaxRetries} after {Delay}ms — {ExceptionMessage}"` | Automatic via `ILogger` + `Activity.Current` |
| `OnOpened` | `Error` | `"Circuit breaker OPENED — {FailureThreshold} failures in {SamplingDuration}s"` | Same |
| `OnHalfOpened` | `Information` | `"Circuit breaker HALF-OPENED — probe request allowed"` | Same |
| `OnClosed` | `Information` | `"Circuit breaker CLOSED — normal operation resumed"` | Same |

### Not Applicable (unchanged from M1.3)
- **DB resilience:** Already configured in UserService via `ResilientAppDbContext` — unchanged this milestone.
- **HTTP resilience:** Already configured in Gateway + Worker via `ResilienceDelegatingHandler` — unchanged this milestone.
- **Async messaging:** N/A — deferred to Phase 02.

---

## Dependency Check

| Resource | Required? | Status |
|---|---|---|
| PostgreSQL | Yes — data integration tests need DB | ✅ Provisioned (Docker Compose); ❌ Missing in CI `build-and-test` → **Resolved in Unit 4** |
| Kendo.Shared | Yes — host for OTel extension | ✅ Exists (Day 03) |
| Gateway | Yes — add `AddKendoObservability()` | ✅ Exists (Day 01) |
| UserService | Yes — add `AddKendoObservability()` | ✅ Exists (Day 01) |
| Worker | Yes — add `AddKendoObservability()` | ✅ Exists (Day 01) |
| NuGet feeds | Yes — OTel packages on NuGet.org | ✅ Standard |
| RabbitMQ | No | Deferred to Phase 02 |
| Redis | No | Deferred to Phase 03 |

---

## Open Questions / Clarifications

- **OTLP vs Console exporter:** Console exporter is sufficient for local dev and CI verification. In production (Phase 02+), the same extension should conditionally register an OTLP exporter based on `OTEL_EXPORTER_OTLP_ENDPOINT` env var presence — discuss with DevOps when that phase begins.
- **OpenTelemetry.Instrumentation.AspNetCore missing from roadmap specificity:** The roadmap says "traces and structured logs flowing to console/collector." For .NET 10, that means `AddAspNetCoreInstrumentation()` auto-captures request-level spans. No manual span creation needed beyond the Polly callback enrichment planned here. This is the standard approach and matches the acceptance criteria precisely.
- **carry-forward CI PostgreSQL container:** Resolved as Unit 4 in this spec. The `services` block creates an ephemeral PostgreSQL 16 instance per CI run, scoped to the job. Connection string is passed via env var matching the existing `ConnectionStrings__DefaultConnection` convention.
