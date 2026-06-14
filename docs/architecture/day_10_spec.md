# Day 10 — Architecture Specification

## Milestone Scope

- **Milestone:** M2.5 — Outbox pattern: transactional outbox table in EF Core; relay job publishes pending messages atomically
- **Roadmap phase:** 02 — Asynchronous Decoupling
- **Components touched:**
  - `UserService` — new `OutboxMessage` entity + `OutboxMessages` table (in `AppDbContext`); new `OutboxRelayService` hosted service; refactor `UsersController` to write to outbox instead of direct Rebus send
  - `Kendo.Shared` — new `KendoMessageSerializer` helper for outbox serialization/deserialization
  - `Worker` — no changes (unchanged consumer-side idempotency; outbox is producer-side only)
  - `Gateway` — no changes
- **Explicitly out of scope:**
  - M2.6 — `traceparent` propagation across producer and consumer
  - M3.1 — Load balancer / reverse proxy
  - M3.2 — Stateless validation / sticky session removal

## Layer Changes

### UserService (`src/UserService/`)

**New file:** `Data/OutboxMessage.cs`
Entity representing a domain event awaiting publication via Rebus. Written atomically with the business transaction; processed asynchronously by the relay.

**Modified:** `Data/AppDbContext.cs`
Add `DbSet<OutboxMessage>` with table configuration (PK, indexes, defaults).

**New directory:** `Services/`
- `Services/OutboxRelayService.cs` — `BackgroundService` that polls the outbox table, publishes pending messages via `IBus`, and marks them as processed.

**Modified:** `Program.cs`
Register `OutboxRelayService` as a hosted service.

**Modified:** `Controllers/UsersController.cs`
Replace direct `IBus.Send()` call with `OutboxMessage` record creation. The controller no longer depends on `IBus` directly — it writes to the outbox table in the same transaction as user creation.

**New migration:** EF Core migration for `OutboxMessages` table.

### Kendo.Shared (`src/Shared/Messaging/`)

**New file:** `KendoMessageSerializer.cs`
Static helper that serializes a `KendoMessage` to/from JSON for outbox storage. Stores the assembly-qualified type name alongside the JSON payload.

### Worker / Gateway
**No changes.** The Worker continues to consume `UserCreatedEvent` from Rebus with idempotency enforcement. The outbox is purely a producer-side change — the Worker receives events exactly as before.

## Data Contracts

### Entity: `OutboxMessage`

| Column | Type | Constraints |
|---|---|---|
| `Id` | `uuid` | PK, default `gen_random_uuid()` |
| `MessageId` | `uuid` | NOT NULL — original `KendoMessage.MessageId` for idempotency |
| `MessageType` | `text` | NOT NULL — assembly-qualified type name (e.g., `"Kendo.Shared.Messaging.UserCreatedEvent, Kendo.Shared"`) |
| `Payload` | `text` | NOT NULL — JSON-serialized message body |
| `CreatedAt` | `timestamptz` | NOT NULL, default `now()` |
| `ProcessedAt` | `timestamptz` | NULLABLE — set when the relay successfully publishes via Rebus |
| `RetryCount` | `integer` | NOT NULL, default 0 — incremented on each failed publish attempt |
| `LastError` | `text` | NULLABLE — stores the last error message for diagnostics |

```csharp
namespace Kendo.UserService.Data;

public class OutboxMessage
{
    public Guid Id { get; set; }
    public Guid MessageId { get; set; }
    public string MessageType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; set; }
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
}
```

### Indexes

- `IX_OutboxMessages_Unprocessed`: filtered index `WHERE "ProcessedAt" IS NULL` on `(CreatedAt)` — enables efficient relay polling for pending messages.
- `IX_OutboxMessages_MessageId`: unique index on `(MessageId)` — prevents duplicate outbox entries for the same domain event.

### Serialization Contract: `KendoMessageSerializer`

```csharp
namespace Kendo.Shared.Messaging;

public static class KendoMessageSerializer
{
    /// <summary>
    /// Serializes a KendoMessage to an outbox entry payload (JSON).
    /// </summary>
    public static string Serialize(KendoMessage message);

    /// <summary>
    /// Returns the assembly-qualified type name for the message.
    /// </summary>
    public static string GetMessageType(KendoMessage message);

    /// <summary>
    /// Deserializes a KendoMessage from its stored type name and JSON payload.
    /// Returns null if the type cannot be resolved.
    /// </summary>
    public static KendoMessage? Deserialize(string messageType, string payload);
}
```

**Behavior:**
- `Serialize`: Uses `System.Text.Json.JsonSerializer` with `camelCase` naming policy.
- `GetMessageType`: Returns `message.GetType().AssemblyQualifiedName` (short form, not full — strips version/culture/public-key for flexibility).
- `Deserialize`: Resolves `Type.GetType(messageType)` → `JsonSerializer.Deserialize(payload, resolvedType)` → casts to `KendoMessage`.

### Configuration

UserService `appsettings.json` additions:
```json
{
  "Relays": {
    "Outbox": {
      "PollingIntervalSeconds": 5,
      "BatchSize": 20,
      "MaxRetries": 5
    }
  }
}
```

| Key | Default | Description |
|---|---|---|
| `Relays__Outbox__PollingIntervalSeconds` | 5 | How often the relay polls for pending messages |
| `Relays__Outbox__BatchSize` | 20 | Max messages to fetch per poll cycle |
| `Relays__Outbox__MaxRetries` | 5 | Max delivery attempts before skipping a message |

## Implementation Plan (Commit Units)

### Unit 1 — Outbox entity + KendoMessageSerializer + migration

- **Files:**
  - `src/UserService/Data/OutboxMessage.cs` — new `OutboxMessage` entity
  - `src/Shared/Messaging/KendoMessageSerializer.cs` — new serialization helper
  - `src/UserService/Data/AppDbContext.cs` — add `DbSet<OutboxMessage>`, entity config with indexes
  - EF Core migration: `dotnet ef migrations add AddOutboxMessages --project src/UserService`
- **Gate command:**
  ```bash
  dotnet build --no-restore 2>&1 && \
  dotnet ef migrations has-pending-model-changes --project src/UserService 2>&1
  ```
- **Commit message:**
  ```
  feat(shared): add OutboxMessage entity, KendoMessageSerializer, and EF Core migration

  Day 10 — unit 1 of 5 | Milestone M2.5
  ```

### Unit 2 — OutboxRelayService: polling BackgroundService

- **Files:**
  - `src/UserService/Services/OutboxRelayService.cs` — new `BackgroundService` that polls the outbox table, deserializes messages, publishes via `IBus`, and marks as processed
  - `src/UserService/Program.cs` — register `OutboxRelayService` as hosted service
- **Gate command:** `dotnet build --no-restore 2>&1`
- **Commit message:**
  ```
  feat(userservice): add OutboxRelayService background relay for pending messages

  Day 10 — unit 2 of 5 | Milestone M2.5
  ```

### Unit 3 — Refactor UsersController to write to outbox instead of direct Rebus send

- **Files:**
  - `src/UserService/Controllers/UsersController.cs` — remove `IBus` dependency; replace direct `_bus.Send()` with `OutboxMessage` creation in `UserRepository` (or as a new `OutboxRepository`); read outbox from the same transaction
  - `src/UserService/Data/UserRepository.cs` — optionally add outbox write within the same transaction (cleaner to add an `OutboxRepository` to keep SRP)
  - `src/UserService/Data/OutboxRepository.cs` — new thin repository for writing outbox records
- **Gate command:** `dotnet build --no-restore 2>&1`
- **Commit message:**
  ```
  feat(userservice): refactor UsersController to use outbox table instead of direct Rebus send

  Day 10 — unit 3 of 5 | Milestone M2.5
  ```

### Unit 4 — Tests: outbox relay, serialization, controller refactor

- **Files:**
  - `tests/Kendo.Tests/UserService/OutboxMessageSerializerTests.cs` — serialize/deserialize round-trip, unknown type handling
  - `tests/Kendo.Tests/UserService/OutboxRelayServiceTests.cs` — relay polls pending messages, publishes on Rebus, marks processed, handles publish failure, respects MaxRetries, respects BatchSize
  - `tests/Kendo.Tests/UserService/OutboxRepositoryTests.cs` — outbox record creation, duplicate MessageId prevention
  - `tests/Kendo.Tests/UserService/UsersControllerTests.cs` — update existing tests to verify outbox write instead of direct bus send; add test verifying `CreateUserRequest` produces `OutboxMessage` with correct `MessageType` and payload
- **Gate command:** `dotnet test tests/Kendo.Tests/Kendo.Tests.csproj --no-restore --filter "Category=Unit" 2>&1`
- **Commit message:**
  ```
  test(userservice): add outbox relay, serializer, and updated controller tests

  Day 10 — unit 4 of 5 | Milestone M2.5
  Coverage: reported %
  Lint: clean
  ```

### Unit 5 — Runbook + CI smoke test

- **Files:**
  - `ops/runbooks/day_10_runbook.md` — outbox recovery procedure, relay tuning, manual outbox drain
  - `.github/workflows/ci.yml` — verify new outbox tests are included in CI run (add `Category=Outbox` if needed)
- **Gate command:** `dotnet test tests/Kendo.Tests/Kendo.Tests.csproj --no-restore --filter "Category=Unit" 2>&1`
- **Commit message:**
  ```
  docs: add outbox pattern runbook with recovery and tuning guide

  Day 10 — unit 5 of 5 | Milestone M2.5
  ```

## Success Checklist

- [ ] `POST /api/users` creates a user and an `OutboxMessage` record in the **same database transaction** — if either fails, both roll back (verified by test: simulate DB failure mid-transaction → no user + no outbox record)
- [ ] `OutboxMessage` record contains: valid `Id`, `MessageId`, `MessageType` (assembly-qualified), `Payload` (JSON), `CreatedAt`, `ProcessedAt = null` (verified by test)
- [ ] `OutboxRelayService` polls `OutboxMessages` where `ProcessedAt IS NULL` ordered by `CreatedAt`, up to `BatchSize` per cycle (verified by test)
- [ ] Relay publishes each pending message via `IBus.Send()` using the deserialized type (verified by test: mock bus)
- [ ] On successful publish, relay sets `ProcessedAt` on the outbox record (verified by test)
- [ ] On publish failure, relay increments `RetryCount` and records `LastError` — does not crash, continues to next message (verified by test)
- [ ] Messages whose `RetryCount` exceeds `MaxRetries` are skipped (not retried infinitely) — logged at `LogLevel.Error` (verified by test)
- [ ] Outbox relay respects `Relays__Outbox__PollingIntervalSeconds` between poll cycles (verified by test)
- [ ] `KendoMessageSerializer.Serialize` + `Deserialize` round-trips correctly for `UserCreatedEvent` (verified by test)
- [ ] All existing tests continue to pass after outbox refactor (regression verification)
- [ ] `docker compose up` starts all services; all `/health/ready` probes pass
- [ ] No raw Exception message returned to client on any error path — all errors flow through RFC 7807 middleware (unchanged)

## Resilience Mandate

### UserService — `OutboxRelayService`

**Database reads (polling):**
- Uses `ResilientAppDbContext` or `WorkerResilientDbContext`-equivalent for DB reads (retry + circuit breaker inherited from M1.3 baseline).
- A transient DB failure during polling simply skips that cycle — the relay logs a warning and retries next interval.

**Publishing via `IBus.Send()`:**
- Each publish attempt is wrapped in a try/catch. If `IBus.Send` throws (ASB unavailable, transient failure), the relay catches, increments `RetryCount`, records `LastError`, and continues to the next message.
- No circuit breaker on the relay itself — it's a polling loop. Transient ASB failures are handled by retrying on the next poll cycle.
- After `MaxRetries` failures, the message is permanently skipped (logged at `LogLevel.Error`). Manual recovery from the outbox table is documented in the runbook.

**Concurrency safety (multi-replica):**
- Outbox relay uses an **optimistic concurrency** model: it reads pending rows, processes each, and updates with `WHERE ProcessedAt IS NULL`. If another replica processes the same row first, the UPDATE affects 0 rows and the current replica skips it.
- Double-publishing is safe because the Worker's `UserCreatedEventHandler` enforces idempotency via `MessageId` — duplicates are silently discarded.
- Cross-replica double-processing is therefore harmless. A production enhancement would add `FOR UPDATE SKIP LOCKED` — noted as future work.

### UsersController — write path

**Outbox write is within transaction scope:**
- The user creation and outbox record creation occur within `ResilientAppDbContext.ExecuteAsync` (retry + circuit breaker inherited).
- If the DB is unavailable, retries are exhausted → `BrokenCircuitException` → middleware returns `503 Service Unavailable` with RFC 7807 body. No partial writes — the transaction ensures atomicity.

### No changes to Worker resilience

The Worker's `UserCreatedEventHandler` idempotency, `DeadLetteredMessage` handler, and `DlqDepthMonitor` remain unchanged.
