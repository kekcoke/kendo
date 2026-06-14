# Day 09 — Architecture Specification

## Milestone Scope

- **Milestone:** M2.4 — Dead Letter Queue: DLQ consumer implemented; alert fires when DLQ depth exceeds threshold
- **Roadmap phase:** 02 — Asynchronous Decoupling
- **Components touched:**
  - `Kendo.Shared.Messaging` — new `DeadLetteredMessage` record type; new `KendoRebusDlqConfiguration` extension for DLQ consumer registration
  - `Worker` — new `DeadLetterHandler` (Rebus handler for DLQ); new `DlqDepthMonitor` background service (periodic depth check + alert); new `WorkerDbContext` migration (add `DeadLetterRecords` table or reuse idempotency tracking)
  - `Gateway` / `UserService` — no changes (producer side unchanged)
- **Explicitly out of scope:**
  - M2.5 — Transactional outbox pattern (next milestone)
  - M2.6 — `traceparent` propagation across producer and consumer
  - Re-driving DLQ messages back to main queue (manual intervention only)
  - Azure Monitor / PagerDuty alert integration (structured `ILogger.Error` + health endpoint exposure)

## Layer Changes

### Kendo.Shared (`src/Shared/Messaging/`)
New message type:
- `DeadLetteredMessage.cs` — record representing a message that landed on the DLQ, wrapping the original message headers + metadata

New configuration extension:
- `KendoRebusDlqConfiguration.cs` — `AddKendoRebusDlqConsumer()` extension method that registers a Rebus consumer on the ASB DLQ path (`kendo-events/$DeadLetterQueue`)

### Worker (`src/Worker/`)
- New `Handlers/DeadLetterHandler.cs` — Rebus `IHandleMessages<DeadLetteredMessage>` that logs every DLQ message with original headers, routing info, and error details
- New `Services/DlqDepthMonitor.cs` — periodic `BackgroundService` (configurable interval, default 60s) that queries ASB DLQ depth and logs `LogLevel.Error` + sets a health check critical flag when depth exceeds configurable threshold (default 5)
- New `Data/DlqRecord.cs` entity + update `WorkerDbContext` — persistent store for DLQ events (optional but recommended for alerting reference)
- `Program.cs` — register DLQ consumer, register `DlqDepthMonitor` as hosted service, register updated `WorkerDbContext`

### UserService / Gateway
- **No changes** — producers continue publishing normally; failed messages are handled at the consumer/DLQ boundary

## Data Contracts

### Message: `DeadLetteredMessage`

```csharp
namespace Kendo.Shared.Messaging;

/// <summary>
/// Wraps metadata about a message that was moved to the Dead Letter Queue.
/// Rebus delivers the original message body + headers to the DLQ handler.
/// This type serves as a typed marker for the handler registration.
/// </summary>
public sealed record DeadLetteredMessage : KendoMessage
{
    /// <summary>
    /// The original message ID from the failed message.
    /// </summary>
    public Guid OriginalMessageId { get; init; }

    /// <summary>
    /// The original message type (e.g. "UserCreatedEvent").
    /// </summary>
    public string OriginalMessageType { get; init; } = string.Empty;

    /// <summary>
    /// Reason for dead-lettering (from ASB headers).
    /// </summary>
    public string? DeadLetterReason { get; init; }

    /// <summary>
    /// Error description from ASB headers.
    /// </summary>
    public string? DeadLetterErrorDescription { get; init; }

    /// <summary>
    /// How many times delivery was attempted before dead-lettering.
    /// </summary>
    public int DeliveryCount { get; init; }

    /// <summary>
    /// When the original message was enqueued (from headers).
    /// </summary>
    public DateTimeOffset? EnqueuedTime { get; init; }
}
```

### Entity: `DlqRecord` (optional tracking)

| Column | Type | Constraints |
|---|---|---|
| `Id` | `uuid` | PK, default `gen_random_uuid()` |
| `OriginalMessageId` | `uuid` | NOT NULL |
| `OriginalMessageType` | `text` | NOT NULL |
| `DeadLetterReason` | `text` | NULLABLE |
| `DeadLetterErrorDescription` | `text` | NULLABLE |
| `DeliveryCount` | `integer` | NOT NULL, default 0 |
| `EnqueuedTime` | `timestamptz` | NULLABLE |
| `DetectedAt` | `timestamptz` | NOT NULL, default `now()` |
| `Alerted` | `boolean` | NOT NULL, default `false` |

```csharp
namespace Kendo.Worker.Data;

public class DlqRecord
{
    public Guid Id { get; set; }
    public Guid OriginalMessageId { get; set; }
    public string OriginalMessageType { get; set; } = string.Empty;
    public string? DeadLetterReason { get; set; }
    public string? DeadLetterErrorDescription { get; set; }
    public int DeliveryCount { get; set; }
    public DateTimeOffset? EnqueuedTime { get; set; }
    public DateTimeOffset DetectedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool Alerted { get; set; }
}
```

### Configuration

Worker `appsettings.json` additions:
```json
{
  "Rebus": {
    "DlqConsumerEnabled": false,
    "DlqThreshold": 5,
    "DlqPollingIntervalSeconds": 60,
    "DlqConnectionString": ""  // defaults to Rebus__ConnectionString if empty
  }
}
```

The DLQ consumer is disabled by default (`DlqConsumerEnabled: false`) — opted in via environment variable override. This prevents the DLQ handler from activating in local dev unless explicitly configured.

### Rebus Topology — DLQ

| Resource | Type | Name | Purpose |
|---|---|---|---|
| Dead Letter Queue | ASB DLQ (auto) | `kendo-events/$DeadLetterQueue` | ASB automatically moves messages to this sub-queue after `MaxDeliveryCount` (default 10) |
| DLQ Consumer | Rebus consumer | Listens on `kendo-events/$DeadLetterQueue` | Processes dead-lettered messages, logs, stores, triggers alert |

ASB automatically creates a DLQ for every queue. The DLQ path follows the convention `{queueName}/$DeadLetterQueue`. Rebus can consume from this path by specifying it as the queue name.

## Implementation Plan (Commit Units)

### Unit 1 — Shared types: DeadLetteredMessage + DLQ consumer configuration extension

- **Files:**
  - `src/Shared/Messaging/DeadLetteredMessage.cs` — new record type
  - `src/Shared/Messaging/KendoRebusDlqConfiguration.cs` — new `AddKendoRebusDlqConsumer()` extension method
- **Gate command:** `dotnet build src/Shared/Kendo.Shared.csproj --no-restore 2>&1`
- **Commit message:**
  ```
  feat(shared): add DeadLetteredMessage record + DLQ consumer registration extension

  Day 09 — unit 1 of 5 | Milestone M2.4
  ```

### Unit 2 — Worker: DeadLetterHandler + DlqRecord entity + WorkerDbContext update

- **Files:**
  - `src/Worker/Handlers/DeadLetterHandler.cs` — Rebus handler for DLQ messages
  - `src/Worker/Data/DlqRecord.cs` — new entity for DLQ event persistence
  - `src/Worker/Data/WorkerDbContext.cs` — add `DbSet<DlqRecord>` configuration
  - EF Core migration: `dotnet ef migrations add AddDlqRecords`
- **Gate command:** `dotnet build src/Worker/Kendo.Worker.csproj --no-restore 2>&1 && dotnet ef migrations has-pending-model-changes --project src/Worker 2>&1`
- **Commit message:**
  ```
  feat(worker): implement DeadLetterHandler with DLQ record persistence

  Day 09 — unit 2 of 5 | Milestone M2.4
  ```

### Unit 3 — Worker: DlqDepthMonitor background service with threshold alerting

- **Files:**
  - `src/Worker/Services/DlqDepthMonitor.cs` — periodic ASB DLQ depth checker
  - `src/Worker/Program.cs` — register `DlqDepthMonitor` as hosted service, register DLQ consumer (conditional on config)
- **Gate command:** `dotnet build src/Worker/Kendo.Worker.csproj --no-restore 2>&1`
- **Commit message:**
  ```
  feat(worker): add DlqDepthMonitor background service with threshold-based alerting

  Day 09 — unit 3 of 5 | Milestone M2.4
  ```

### Unit 4 — Tests: DLQ handler, DLQ monitor, alerting logic

- **Files:**
  - `tests/Kendo.Tests/Worker/DeadLetterHandlerTests.cs` — tests for DLQ message processing, persistence, duplicate handling
  - `tests/Kendo.Tests/Worker/DlqDepthMonitorTests.cs` — tests for depth check, threshold alert, below-threshold suppression
- **Gate command:** `dotnet test tests/Kendo.Tests/Kendo.Tests.csproj --no-restore --filter "Category=Unit" 2>&1`
- **Commit message:**
  ```
  test(worker): add DeadLetterHandler and DlqDepthMonitor unit tests

  Day 09 — unit 4 of 5 | Milestone M2.4
  Coverage: reported %
  Lint: clean
  ```

### Unit 5 — Runbook + CI integration

- **Files:**
  - `ops/runbooks/day_09_runbook.md` — DLQ recovery procedure, threshold tuning, alert response
  - `.github/workflows/ci.yml` — no changes needed if Worker tests already run (verify DLQ tests are included in existing CI filter)
- **Gate command:** `dotnet test tests/Kendo.Tests/Kendo.Tests.csproj --no-restore --filter "Category=Unit" 2>&1`
- **Commit message:**
  ```
  docs: add DLQ runbook with recovery procedure and alert response steps

  Day 09 — unit 5 of 5 | Milestone M2.4
  ```

## Success Checklist

- [ ] When a message delivery fails and ASB moves it to the DLQ, the `DeadLetterHandler` in the Worker receives it, logs the headers and error details, and persists a `DlqRecord` (verified by test: simulate message → DLQ consumption)
- [ ] `DlqRecord` contains: `OriginalMessageId`, `OriginalMessageType`, `DeadLetterReason`, `DeadLetterErrorDescription`, `DeliveryCount`, `EnqueuedTime`, `DetectedAt` (verified by test)
- [ ] `DlqDepthMonitor` queries ASB DLQ depth at the configured `PollingIntervalSeconds` interval (verified by test: simulate ASB management client)
- [ ] When DLQ depth exceeds the configured `DlqThreshold` (default 5), `DlqDepthMonitor` logs an `ILogger.LogError` message with the current depth, threshold, and a list of message types in the DLQ (verified by test)
- [ ] When DLQ depth is at or below the configured threshold, no alert-level log is emitted (verified by test)
- [ ] DLQ consumer is **disabled by default** — only activates when `Rebus__DlqConsumerEnabled: true` is set (verified by test)
- [ ] All existing tests (50/50) continue to pass after changes (regression verification)
- [ ] `docker compose up` starts all services; all `/health/ready` probes pass
- [ ] No raw Exception message returned to client on any error path — all errors flow through RFC 7807 middleware (unchanged — no new endpoints)

## Resilience Mandate

### Worker — `DeadLetterHandler`

**Operation:** Persisting `DlqRecord` to database, logging DLQ details.
- **Circuit Breaker:** 3 consecutive failures → 30s break (inherited from M1.3 baseline)
- **Retry:** 3 attempts, exponential backoff with jitter (inherited)
- **Failure behavior:** If persistence fails after retry exhaustion, the handler logs a structured error (including the DLQ metadata) at `LogLevel.Critical` and the Rebus consumer completes the message (acknowledges it) to prevent re-delivery loops. The DLQ metadata is preserved in the log — manual recovery can re-queue from structured logs.

**DLQ handling is fail-safe:** The handler consumes messages from the DLQ. If processing fails, the message is acknowledged anyway (log-based recovery). This prevents infinite re-delivery loops on the DLQ itself (a message that fails during DLQ processing should not be re-dead-lettered).

### Worker — `DlqDepthMonitor`

**Operation:** Periodically queries ASB management API for DLQ depth.
- **No Circuit Breaker:** The monitor is a polling loop — a transient failure to query depth simply skips that cycle and retries next interval. The `ILogger.LogWarning` is emitted on failures to query depth.
- **Retry:** 1 attempt per cycle — next cycle retries naturally.
- **Alert dedup:** The monitor tracks the "last alerted" state. If DLQ depth was above threshold and an alert was already fired, subsequent cycles above threshold do NOT re-fire until the depth drops below threshold and exceeds it again (rate-limited alerting prevents log spam).

### DLQ Consumer Registration

- `AddKendoRebusDlqConsumer()` is a **separate** Rebus bus instance or a secondary consumer on the same bus, depending on Rebus limitations. If Rebus supports multiple queues on one bus instance, it uses the same bus. If not, a lightweight second bus instance is registered (no workers on the main queue — separate `NumberOfWorkers: 1` for DLQ).
- The DLQ consumer uses `NumberOfWorkers: 1` (DLQ is a low-volume operational queue — no parallelism needed).
- The DLQ consumer is **disabled by default** (`Rebus__DlqConsumerEnabled: false`) to prevent accidental DLQ consumption in dev environments without ASB.

### Fallback for missing ASB connection string

Both the DLQ consumer and the `DlqDepthMonitor` gracefully skip initialization when `Rebus__ConnectionString` is empty/missing (same pattern as existing `AddKendoRebus`), enabling local development without Azure Service Bus.
