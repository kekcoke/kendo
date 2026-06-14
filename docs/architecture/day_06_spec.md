# Day 06 — Architecture Specification

## Milestone Scope

- **Milestone:** M2.1 — Rebus + Azure Service Bus wired: producer and consumer registered in DI; bus starts cleanly
- **Roadmap phase:** 02 — Asynchronous Decoupling
- **Components touched:**
  - `Kendo.Shared` — new `Messaging/` module (Rebus registration extension, shared message types, topology config)
  - `Gateway` — wire Rebus producer into DI
  - `UserService` — wire Rebus producer into DI
  - `Worker` — wire Rebus consumer into DI
- **Explicitly out of scope:**
  - M2.2 — First async endpoint (`202 Accepted` pattern) — deferred to Day 07
  - M2.3 — Background consumer with idempotency key enforcement
  - M2.4–M2.6 — DLQ, Outbox, traceparent propagation

## Layer Changes

### Kendo.Shared (`src/Shared/`)
New `Messaging/` directory:
- `KendoRebusConfiguration.cs` — extension method `AddKendoRebus()` that registers Rebus with Azure Service Bus transport, configures topology (single topic/queue for Phase 02), and registers shared message handlers
- `KendoMessage.cs` — base message marker interface/abstract class (ensures all domain messages share a common type)
- `SharedMessageTypes.cs` — placeholder for shared event schemas (reserved for M2.2 when first message types are defined)

### Gateway (`src/Gateway/`)
- Add `Rebus.ServiceProvider` and `Rebus.AzureServiceBus` NuGet references to `Kendo.Gateway.csproj`
- `Program.cs` — add `builder.Services.AddKendoRebus(rebusRole: "producer")` call

### UserService (`src/UserService/`)
- Add same NuGet references to `Kendo.UserService.csproj`
- `Program.cs` — add `builder.Services.AddKendoRebus(rebusRole: "producer")` call

### Worker (`src/Worker/`)
- Add same NuGet references to `Kendo.Worker.csproj`
- `Program.cs` — add `builder.Services.AddKendoRebus(rebusRole: "consumer")` call (consumer mode auto-starts the bus in background)

### Configuration (`appsettings` per service)
Each service's `appsettings.Development.json` and `appsettings.json` gain a `Rebus` section with:
```json
{
  "Rebus": {
    "ConnectionString": "Endpoint=sb://...",        // externalized via env var: Rebus__ConnectionString
    "QueueName": "kendo-events",
    "NumberOfWorkers": 3,
    "MaxParallelism": 10
  }
}
```

## Data Contracts

### Rebus Topology — Phase 02

| Resource | Type | Name | Purpose |
|---|---|---|---|
| Queue | Azure Service Bus Queue | `kendo-events` | All Phase 02 domain events delivered to Worker consumers |
| Connection string | Externalized via env var | `Rebus__ConnectionString` | ASB namespace connection string |

### `KendoRebusConfiguration` Extension API

```csharp
namespace Kendo.Shared.Messaging;

public static class KendoRebusConfiguration
{
    /// <summary>
    /// Registers Rebus with Azure Service Bus transport.
    /// </summary>
    /// <param name="rebusRole">"producer" — bus registers but does not auto-start consumers.
    /// "consumer" — bus registers and auto-starts polling the kendo-events queue.</param>
    public static IServiceCollection AddKendoRebus(
        this IServiceCollection services,
        IConfiguration configuration,
        string rebusRole)
    { ... }
}
```

**Behavior:**
- Reads `Rebus__ConnectionString` from configuration/environment
- "producer" mode: configures Rebus with Azure Service Bus transport, sets `MaxParallelism(1)` and does NOT auto-create any consumer handlers (bus is available for `await bus.Send()` only)
- "consumer" mode: configures Rebus with Azure Service Bus transport + the `kendo-events` queue, sets `NumberOfWorkers(3)`, registers any `IHandleMessages<T>` implementations found in the assembly via automatic discovery
- If `Rebus__ConnectionString` is empty/missing → bus registration is **gracefully skipped** with a log warning (enables local dev without ASB)

### `KendoMessage` Base

```csharp
namespace Kendo.Shared.Messaging;

/// <summary>
/// Base marker for all Kendo domain messages.
/// </summary>
public abstract record KendoMessage
{
    public Guid MessageId { get; init; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
```

## Implementation Plan (Commit Units)

### Unit 1 — Kendo.Shared messaging module: base types, Rebus registration extension

- **Files:**
  - `src/Shared/Messaging/KendoMessage.cs` — abstract record base
  - `src/Shared/Messaging/KendoRebusConfiguration.cs` — `AddKendoRebus()` extension with producer/consumer modes
- **Gate command:** `dotnet build src/Shared/Kendo.Shared.csproj --no-restore 2>&1`
- **Commit message:**
  ```
  feat(shared): add Rebus messaging module with producer/consumer registration

  Day 06 — unit 1 of 4 | Milestone M2.1
  ```

### Unit 2 — Add Rebus packages + wire into Gateway and UserService (producers)

- **Files:**
  - `src/Gateway/Kendo.Gateway.csproj` — add `Rebus.ServiceProvider` + `Rebus.AzureServiceBus` package references
  - `src/Gateway/Program.cs` — add `using Kendo.Shared.Messaging;` + `builder.Services.AddKendoRebus(builder.Configuration, "producer");`
  - `src/UserService/Kendo.UserService.csproj` — add same package references
  - `src/UserService/Program.cs` — add `using Kendo.Shared.Messaging;` + `builder.Services.AddKendoRebus(builder.Configuration, "producer");`
- **Gate command:** `dotnet build src/Gateway/Kendo.Gateway.csproj --no-restore 2>&1 && dotnet build src/UserService/Kendo.UserService.csproj --no-restore 2>&1`
- **Commit message:**
  ```
  feat(services): wire Rebus producer into Gateway and UserService

  Day 06 — unit 2 of 4 | Milestone M2.1
  ```

### Unit 3 — Add Rebus packages + wire into Worker (consumer)

- **Files:**
  - `src/Worker/Kendo.Worker.csproj` — add `Rebus.ServiceProvider` + `Rebus.AzureServiceBus` package references
  - `src/Worker/Program.cs` — add `using Kendo.Shared.Messaging;` + `builder.Services.AddKendoRebus(builder.Configuration, "consumer");`
- **Gate command:** `dotnet build src/Worker/Kendo.Worker.csproj --no-restore 2>&1`
- **Commit message:**
  ```
  feat(worker): wire Rebus consumer into Worker

  Day 06 — unit 3 of 4 | Milestone M2.1
  ```

### Unit 4 — Smoke test: Rebus bus starts cleanly in producer and consumer modes

- **Files:**
  - `tests/Kendo.Tests/Messaging/RebusRegistrationTests.cs` — tests that verify DI registration succeeds, bus resolves without throwing, and graceful-skip works when connection string is missing
- **Gate command:** `dotnet test tests/Kendo.Tests/Kendo.Tests.csproj --no-restore --filter "Category=Messaging" 2>&1`
- **Commit message:**
  ```
  test(messaging): add Rebus registration smoke tests

  Day 06 — unit 4 of 4 | Milestone M2.1
  Coverage: reported %
  ```

## Success Checklist

- [ ] `AddKendoRebus` with `rebusRole: "producer"` registers without error and resolves `IBus` (verified by test)
- [ ] `AddKendoRebus` with `rebusRole: "consumer"` registers without error and resolves `IBus` (verified by test)
- [ ] When `Rebus__ConnectionString` is empty/missing, the bus registration is skipped with a warning log — no crash (verified by test)
- [ ] Gateway builds and runs with Rebus registered (verified by build + smoke resolution)
- [ ] UserService builds and runs with Rebus registered (verified by build + smoke resolution)
- [ ] Worker builds and runs with Rebus registered (verified by build + smoke resolution)
- [ ] All existing tests (39/39) continue to pass after adding Rebus packages and registration
- [ ] `docker compose up` still starts all services; all `/health/ready` probes pass (CI regression)

## Resilience Mandate

**N/A — registration-only milestone.**

M2.1 is pure infrastructure wiring — no runtime calls, no message production or consumption logic. Rebus is registered in DI and the bus resolves without throwing. Circuit breakers, retries, and async boundaries are not applicable because no messages are sent or received in this milestone. The `NumberOfWorkers` config (3) and `MaxParallelism` (10) values are set as defaults but are not exercised until M2.2.

The graceful-skip path (missing connection string) ensures local development is not blocked — services start without Rebus when ASB is unavailable.
