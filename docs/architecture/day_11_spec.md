# Day 11 — Architecture Specification

## Milestone Scope

**Milestone:** M2.6 — Async observability: `traceparent` propagated across producer and consumer; correlated spans visible in traces.

**Roadmap phase:** 02 — Asynchronous Decoupling

**Components touched:**
- `UserService` — `OutboxMessage.TraceContext` column (captured at controller-write time); `OutboxRepository.AddAsync()` stores the ambient traceparent; `OutboxRelayService` passes traceparent as Rebus message header at publish time
- `Worker` — `UserCreatedEventHandler.Handle()` extracts `traceparent` from Rebus message headers and creates a child `Activity` linked to the producer's trace
- `Kendo.Shared` — no new files; the propagation contract is documented here for cross-reference
- `Gateway` — no changes (not an event producer)

**Explicitly out of scope:**
- M2.5 (Outbox) changes: the outbox pattern is already delivered — this milestone only adds traceparent metadata to the existing outbox flow
- M3.1–M3.6 — Phase 03 concerns (load balancing, chaos suite, rate limiting, graceful shutdown)
- Extracting the traceparent logic into reusable Rebus `IOutgoingMessageInspector` / `IIncomingMessageInspector` — deferred to a future refactor milestone
- Adding `Rebus.OpenTelemetry` NuGet package — manual propagation keeps dependencies minimal and gives explicit control over the header contract

---

## Layer Changes

### UserService (`src/UserService/`)

#### `Data/OutboxMessage.cs` — NEW column: TraceContext

Add a nullable string column to the existing entity:

```csharp
/// <summary>
/// W3C TraceContext traceparent value captured at write time.
/// Enables end-to-end trace correlation between the HTTP request Activity
/// and the consumer-side span in the Worker.
/// </summary>
public string? TraceContext { get; set; }
```

- Type: `text` (nullable) in PostgreSQL
- No index needed — this is metadata, not a query filter
- NOT serialized as part of the message payload (stored alongside, not inside)
- Captured once at outbox record creation — immutable after write

#### `Data/OutboxRepository.cs` — Capture ambient traceparent

In `AddAsync()`, before or after creating the `OutboxMessage`, capture `Activity.Current?.Id`:

```csharp
public virtual async Task AddAsync(KendoMessage message, CancellationToken ct = default)
{
    var activityId = Activity.Current?.Id;   // NEW: capture traceparent at write time

    var outboxMessage = new OutboxMessage
    {
        MessageId = message.MessageId,
        MessageType = KendoMessageSerializer.GetMessageType(message),
        Payload = KendoMessageSerializer.Serialize(message),
        CreatedAt = DateTimeOffset.UtcNow,
        TraceContext = activityId                // NEW
    };

    // ... existing SaveChanges logic ...
}
```

**Why here:** At the time `UsersController.Create()` calls `_outboxRepository.AddAsync()`, the ambient `Activity.Current` is the ASP.NET Core request `Activity` (created by OpenTelemetry's `AddAspNetCoreInstrumentation()`). The trace ID from this Activity is the root trace for the entire request. Capturing it here preserves the link. By the time `OutboxRelayService` publishes the message (milliseconds to seconds later), no ambient Activity exists — the HTTP request has completed.

#### `Services/OutboxRelayService.cs` — Pass traceparent as Rebus header

In `ProcessBatchAsync()`, before calling `bus.Send()`, add headers:

```csharp
// Build optional headers dictionary
var headers = new Dictionary<string, string>();
if (!string.IsNullOrWhiteSpace(outboxMessage.TraceContext))
{
    headers["traceparent"] = outboxMessage.TraceContext;
}

await bus.Send(domainMessage, headers);   // MODIFIED — was bus.Send(domainMessage)
```

**Contract:** The key `"traceparent"` follows the W3C Trace-Context specification format (`00-{trace_id}-{span_id}-{trace_flags}`). Rebus stores this in the Azure Service Bus message's `ApplicationProperties` dictionary as a string key-value pair. On the consumer side, `IMessageContext.Current.Headers["traceparent"]` retrieves it.

**Graceful fallback:** If `TraceContext` is null or empty (messages written before this migration, or created outside an HTTP request), no header is sent. The consumer creates a fresh trace — no crash, no data loss. This is a best-effort correlation enhancement.

### Worker (`src/Worker/`)

#### `Handlers/UserCreatedEventHandler.cs` — Extract traceparent, create child Activity

At the top of `Handle()`, before any processing logic:

```csharp
using var? traceActivity = RebusTraceHelper.StartChildActivity(message);
```

Or inline:

```csharp
// Extract traceparent from Rebus message headers and create a child Activity
using var traceActivity = StartTraceActivity(message);
if (traceActivity is not null)
{
    _logger.LogInformation(
        "Trace correlation: traceId={TraceId}, parentSpanId={ParentSpanId}",
        traceActivity.TraceId, traceActivity.ParentSpanId);
}
```

Where `StartTraceActivity` is a private helper:

```csharp
private static Activity? StartTraceActivity(UserCreatedEvent message)
{
    var context = Rebus.IMessageContext.Current;
    if (context?.Headers.TryGetValue("traceparent", out var traceParent) != true
        || string.IsNullOrWhiteSpace(traceParent))
    {
        return null;  // No parent context — create a brand-new trace (existing behavior)
    }

    var activity = new Activity("UserCreatedEvent.Process");
    activity.SetParentId(traceParent);
    activity.Start();
    return activity;
}
```

**Span details:**
- Display name: `"UserCreatedEvent.Process"` — identifies the handler work in the trace
- Parent ID: the `traceparent` value from the Rebus header (W3C format)
- Tags added automatically: the OpenTelemetry console exporter captures `Activity.TraceId`, `Activity.SpanId`, `Activity.ParentSpanId`, `Activity.DisplayName`, and `Activity.Duration`
- The `_logger` output already includes `Activity.Current.TraceId` via ASP.NET Core's `AddOpenTelemetry` integration, so the trace ID will appear in every log line within the handler's scope

**Cleanup:** The `using` keyword ensures `activity.Stop()` is called when the handler scope exits — releasing the Activity so it's flushed by the console exporter.

---

## Data Contracts

### OutboxMessage — Schema Addition

| Column | Type | Constraints | Nullable | Description |
|---|---|---|---|---|
| `TraceContext` | `text` | none | YES | W3C traceparent value captured at write time (e.g., `00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01`) |

**Migration:** Add a single-column migration. No backfill needed — existing rows remain `NULL`, and the relay gracefully skips them (no crash).

### Rebus Message Header Contract

| Header Key | Value Format | Present When | Consumer Action |
|---|---|---|---|
| `traceparent` | W3C traceparent string | Ambient Activity existed at outbox-write time | Extracted → `Activity.SetParentId()` → child span started |

**Producer-side guarantee:** Header is present iff `Activity.Current?.Id` was non-null at outbox write time.

**Consumer-side contract:** If header is missing, the handler creates a new trace (no parent). If header is present but malformed, `Activity.SetParentId()` throws — the handler wraps this in a try/catch and falls back to a fresh trace (defensive, see Resilience Mandate).

### OpenTelemetry Trace Model — Cross-Process Span Hierarchy

```
HTTP POST /api/users                          [kendo-userservice]
  └─ UsersController.Create (Activity)        trace ID = T, span ID = A
       └─ OutboxRepository.AddAsync            captures traceparent = 00-T-A-01
                                                   
[OutboxRelayService publishes later]           [kendo-userservice]
  └─ bus.Send with header "traceparent": 00-T-A-01
       │
       ▼  Azure Service Bus ── message ──▶    
                                           
UserCreatedEventHandler.Handle               [kendo-worker]
  └─ UserCreatedEvent.Process (Activity)     trace ID = T (same!), span ID = B
     │                                        parent span ID = A
     ├─ Idempotency check                    all logs include traceId = T
     ├─ Database operations
     └─ Status update
```

**Visible in traces:** The console exporter produces:
```
[kendo-userservice] Activity.TraceId: T, Activity.SpanId: A
[kendo-worker]      Activity.TraceId: T, Activity.SpanId: B, Activity.ParentSpanId: A
```

Both log entries share the same trace ID `T`. The Worker's span is explicitly a child of the UserService's span — any trace viewer or grep over logs can correlate the two.

---

## Implementation Plan (Commit Units)

### Unit 1 — Add TraceContext to OutboxMessage entity + capture in OutboxRepository

**Files:**
- `src/UserService/Data/OutboxMessage.cs` — add `TraceContext` property
- `src/UserService/Data/OutboxRepository.cs` — capture `Activity.Current?.Id` in `AddAsync()`

**Gate command:**
```bash
dotnet build src/UserService/Kendo.UserService.csproj --no-restore 2>&1
```

**Commit message:**
```
feat(userservice): add TraceContext column to OutboxMessage, capture ambient traceparent

Day 11 — unit 1 of 4 | Milestone M2.6
```

### Unit 2 — Pass traceparent header in OutboxRelayService publish

**Files:**
- `src/UserService/Services/OutboxRelayService.cs` — build headers dictionary from `outboxMessage.TraceContext`, pass to `bus.Send()`

**No EF migration needed in this unit** — the column is added in Unit 1 (entity) and Unit 4 generates the migration alongside test setup.

**Gate command:**
```bash
dotnet build src/UserService/Kendo.UserService.csproj --no-restore 2>&1
```

**Commit message:**
```
feat(userservice): pass traceparent as Rebus message header in OutboxRelayService

Day 11 — unit 2 of 4 | Milestone M2.6
```

### Unit 3 — Extract traceparent in Worker handler + create child Activity

**Files:**
- `src/Worker/Handlers/UserCreatedEventHandler.cs` — add `StartTraceActivity()` helper; create child Activity from `traceparent` header; wrap in try/catch with fallback to fresh trace

**Gate command:**
```bash
dotnet build src/Worker/Kendo.Worker.csproj --no-restore 2>&1
```

**Commit message:**
```
feat(worker): extract traceparent from Rebus headers, create child Activity in handler

Day 11 — unit 3 of 4 | Milestone M2.6
```

### Unit 4 — Tests + EF migration

**Files:**
- EF Core migration: `dotnet ef migrations add AddTraceContext --project src/UserService`
- `tests/Kendo.Tests/UserService/OutboxRepositoryTests.cs` — add test: `AddAsync_StoresTraceContext`, `AddAsync_TraceContextNullWhenNoActivity`
- `tests/Kendo.Tests/UserService/OutboxRelayServiceTests.cs` — update `ExecuteAsync_PublishesPendingMessages` to verify `traceparent` header; add `ExecuteAsync_PassesTraceparentHeader` test; add `ExecuteAsync_SkipsTraceparentWhenNull` test
- `tests/Kendo.Tests/Worker/UserCreatedEventHandlerTests.cs` — add `Handle_CreatesChildActivityFromTraceparent` test; add `Handle_FallsBackToNewTraceWhenTraceparentMissing` test

**Gate command:**
```bash
dotnet test tests/Kendo.Tests/Kendo.Tests.csproj --no-restore 2>&1
```

**Commit message:**
```
test(shared): add traceparent propagation tests + EF migration

Day 11 — unit 4 of 4 | Milestone M2.6
Coverage: reported %
Lint: clean
```

---

## Success Checklist

- [ ] `OutboxRepository.AddAsync()` stores `Activity.Current?.Id` as `TraceContext` on the `OutboxMessage` record when an ambient Activity exists (verified by unit test)
- [ ] `OutboxRepository.AddAsync()` stores `TraceContext: null` when `Activity.Current` is null (verified by unit test)
- [ ] `OutboxRelayService` passes `traceparent` header to `bus.Send()` when `TraceContext` is non-null (verified by unit test: mock `IBus` captured with header dictionary)
- [ ] `OutboxRelayService` does NOT pass `traceparent` header when `TraceContext` is null/empty (verified by unit test)
- [ ] `UserCreatedEventHandler.Handle()` creates a child `Activity` with the correct trace ID and parent span ID when `traceparent` header is present (verified by unit test: `Activity.Current.TraceId` matches original, `Activity.Current.ParentSpanId` is set)
- [ ] `UserCreatedEventHandler.Handle()` creates a fresh trace (no parent) when `traceparent` header is missing or malformed (verified by unit test)
- [ ] EF migration `AddTraceContext` applies cleanly (verified by `dotnet ef migrations has-pending-model-changes` in CI)
- [ ] All existing tests still pass after adding traceparent columns and logic (regression: 62+ existing tests unchanged)
- [ ] `docker compose up` starts all services; all `/health/ready` probes pass
- [ ] No breaking changes to existing outbox records (null `TraceContext` handled gracefully)

---

## Resilience Mandate

### Traceparent capture — OutboxRepository.AddAsync

**Best-effort correlation, not correctness-critical.** If `Activity.Current` is null, `TraceContext` defaults to null — no crash, no data loss. The consumer falls back to creating a fresh trace.

**No retry concern:** `Activity.Current?.Id` is a property read, not a network call. No failure scenario.

### Traceparent publish — OutboxRelayService

**Existing error handling inherited:** The relay already wraps `bus.Send()` in try/catch with retry count + MaxRetries enforcement (M2.5 spec). Adding headers to the `Dictionary<string, string>` is a pure in-memory operation — no new failure modes. The `headers` dictionary is allocated per message and discarded after publish (no leak).

**Null TraceContext:** Relay skips the header entirely — no special handling needed.

### Traceparent consume — UserCreatedEventHandler

**Defensive parsing:** `Activity.SetParentId(traceParent)` throws `ArgumentNullException` on null and `FormatException` on malformed W3C traceparent strings. The handler wraps the Activity creation in try/catch:

```csharp
private static Activity? StartTraceActivity(UserCreatedEvent message)
{
    var context = Rebus.IMessageContext.Current;
    if (context?.Headers.TryGetValue("traceparent", out var traceParent) != true)
        return null;

    try
    {
        var activity = new Activity("UserCreatedEvent.Process");
        activity.SetParentId(traceParent);
        activity.Start();
        return activity;
    }
    catch (Exception ex)
    {
        // Malformed traceparent — fall back to fresh trace
        Log.Warning("Malformed traceparent '{TraceParent}': {Message}. Creating fresh trace.", traceParent, ex.Message);
        return null;
    }
}
```

**Why this is safe:** The try/catch ensures a malformed header never crashes the handler. Rebus will NOT DLQ or retry the message because of a traceparent parsing error. The handler processes business logic normally — only the trace correlation metadata is lost.

### Async boundaries

**Outbox gap:** There is an unavoidable gap between outbox write (HTTP Activity) and outbox relay publish (background thread). The `TraceContext` bridges this gap by storing the W3C traceparent at write time and restoring it at publish time. This is an explicit, intentional design — not a bug or missing feature.

**No distributed context propagation across timer boundaries:** `System.Threading.ExecutionContext` does NOT flow across `Task.Delay` / timer boundaries. The `TraceContext` string column is the only reliable way to bridge the outbox relay's temporal gap.

### Existing resilience unchanged

The Worker's idempotency enforcement, DeadLetterHandler, crash recovery, and overall resilience infrastructure from M1.3 / M2.3 / M2.4 / M2.5 remain fully operational. The traceparent changes are purely additive — they enhance observability without modifying any existing error handling, retry, or circuit breaker logic.
