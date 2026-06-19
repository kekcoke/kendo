# Architecture Spec — Day 18 — Service Bus: AI Event Contracts

> **Milestone:** M0.3, M0.4 — Service Bus AI event contracts + `kendo-events-ai` queue topology  
> **Roadmap phase:** 00 — Pre-FastAPI Reconciliation (see roadmap header note)  
> **Date:** 2026-06-16  
> **Architect:** Platform Architect (Orchestrator-delegated)  
> **Depends on:** `docs/architecture/fastapi_rag_service_spec.md` § CCD-4; consumes entities from `day_20_spec.md`

---

## Why this spec exists

The FastAPI spec (M5.7–M5.14) introduces 4 new domain events that need to flow through
Azure Service Bus: `EventIngestedEvent` (W1), `EventValidatedEvent` (W2),
`UserEmbeddingUpdatedEvent` (W3/W5), and `NotificationRequestedEvent` (W7). The audit
confirms only `UserCreatedEvent` exists today and only one queue (`kendo-events`) carries
traffic. Per **CCD-4**, AI-mediated flows get a **dedicated** `kendo-events-ai` queue
so a poison message on one queue cannot starve the other and chaos tests stay
independent.

This spec authors the messaging contracts and topology the Gateway, UserService, and
Worker will share. It is the **second** of the four reconciliatory specs and is authored
after `day_20_spec.md` (entities) because the message payloads reference the
`Event`/`EventValidation` entity shapes.

---

## Milestone Scope

- **Roadmap milestone:** M0.3 (new event contracts) + M0.4 (AI queue topology)
- **Components touched:**
  - `src/Shared/Kendo.Shared.Messaging/Events/EventIngestedEvent.cs` — new
  - `src/Shared/Kendo.Shared.Messaging/Events/EventValidatedEvent.cs` — new
  - `src/Shared/Kendo.Shared.Messaging/Events/UserEmbeddingUpdatedEvent.cs` — new
  - `src/Shared/Kendo.Shared.Messaging/Events/NotificationRequestedEvent.cs` — new
  - `src/Shared/Kendo.Shared.Messaging/KendoRebusConfiguration.cs` — extended with `kendo-events-ai`
  - `src/Shared/Kendo.Shared.Messaging/KendoMessage.cs` — unchanged (TraceContext already present, Day 11)
  - `src/Shared/Kendo.Shared.Messaging/KendoMessageSerializer.cs` — unchanged
  - `src/Shared/Kendo.Shared.Messaging/Topology/KendoTopology.cs` — new: queue + DLQ name constants
  - `src/Worker/Workers/DlqDepthMonitorHostedService.cs` — extended to monitor `kendo-events-ai/$DeadLetterQueue`
  - `docker-compose.yml` — second `Rebus__...` env var block for the AI consumer
- **Explicitly out of scope:**
  - The actual handler implementations (covered by `day_19_spec.md`)
  - The Gateway's `IFastAPIClient` (covered by `day_17_spec.md`)
  - The UserService event-controller writes (covered by `day_20_spec.md`)
  - FastAPI service code (covered by `fastapi_rag_service_spec.md`)

---

## Layer Changes

| Layer | Service | Change |
|-------|---------|--------|
| Messaging (contracts) | Kendo.Shared | 4 new `KendoMessage` subclasses |
| Messaging (topology) | Kendo.Shared | `KendoTopology` constants; `KendoRebusConfiguration` registers second consumer for `kendo-events-ai` |
| Observability | Worker | `DlqDepthMonitor` extended to poll both DLQs |
| Configuration | docker-compose | New `KENDO__REBUS__AI__...` env vars; second `Rebus__ConnectionString` block for the Worker |

---

## Data Contracts

### Common base — `KendoMessage` (unchanged, for reference)

`KendoMessage` (Day 06) is the abstract record every domain event inherits from. Day 11
added `TraceContext`. The four new events inherit unchanged — no further base-class
evolution is required for this milestone.

```csharp
public abstract record KendoMessage
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? TraceContext { get; init; }   // W3C traceparent captured at publish time
}
```

### Event: `EventIngestedEvent` (W1)

Emitted by the **Gateway** after W1 (`/v1/rag/query`) returns structured JSON and the
Gateway has validated the JSON shape. Consumed by the **Worker** (`EventIngestedHandler` —
see `day_19_spec.md`), which writes the row to `UserService` and emits the follow-on
`EventValidatedEvent` for W2.

```csharp
public record EventIngestedEvent : KendoMessage
{
    public Guid EventId { get; init; }              // client-generated, idempotency-friendly
    public Guid CreatedByUserId { get; init; }      // owner
    public Guid CorrelationId { get; init; }        // ties to the originating request span
    public string SourceText { get; init; } = "";   // raw free-form text from the user
    public string StructuredJson { get; init; } = "";// W1's structured Event JSON (validated)
    public string EmbeddingModel { get; init; } = "";// e.g. "BGE-large-en-v1.5" or "text-embedding-3-small"
    public int EmbeddingDimensions { get; init; }   // 1024 or 1536
    public List<float> Embedding { get; init; } = new(); // pgvector-ready array
    public DateTimeOffset IngestedAt { get; init; }
}
```

> **Idempotency note:** `EventId` is the idempotency key. The handler upserts
> the `Event` row by `EventId` and skips the embedding write if `event_embeddings`
> already has a row. Re-delivery is safe.

### Event: `EventValidatedEvent` (W2)

Emitted by the **Worker** after `EventIngestedHandler` writes the row AND W2 returns a
validation result. Consumed by the **Worker** (`EventValidatedHandler` — see
`day_19_spec.md`), which persists the `EventValidation` row and may emit a
`NotificationRequestedEvent` for W7.

```csharp
public record EventValidatedEvent : KendoMessage
{
    public Guid EventId { get; init; }
    public Guid CorrelationId { get; init; }
    public bool Ok { get; init; }
    public string ConflictsJson { get; init; } = "[]";
    public string SuggestionsJson { get; init; } = "[]";
    public string? ReasoningTrace { get; init; }
    public DateTimeOffset ValidatedAt { get; init; }
}
```

### Event: `UserEmbeddingUpdatedEvent` (W3, W5)

Emitted by **UserService** after a successful reindex (W5) or after a new user's
profile is embedded for the first time (W3). Consumed by the **Worker**
(`UserEmbeddingUpdatedHandler` — see `day_19_spec.md`), which emits a notification
when the embedding is for a new user and is otherwise a no-op audit log.

```csharp
public record UserEmbeddingUpdatedEvent : KendoMessage
{
    public Guid UserId { get; init; }
    public string EmbeddingModel { get; init; } = "";
    public int EmbeddingDimensions { get; init; }
    public string Trigger { get; init; } = "";  // "first_embed" | "reindex" | "model_swap"
    public DateTimeOffset EmbeddedAt { get; init; }
}
```

### Event: `NotificationRequestedEvent` (W7)

Emitted by the **Worker** after any of: `EventIngestedHandler` (welcome notification),
`EventValidatedHandler` (validation-result notification), or `UserCreatedEventHandler`
(onboarding notification). Consumed by the **Worker** (`NotificationRequestedHandler` —
see `day_19_spec.md`), which calls FastAPI for summarization and emits a
`NotificationDispatchedEvent` (not in this spec — owned by the existing
notification pipeline contract, if any; otherwise left as a future spec).

```csharp
public record NotificationRequestedEvent : KendoMessage
{
    public Guid UserId { get; init; }              // recipient
    public string TemplateId { get; init; } = "";  // e.g. "event.welcome" | "event.conflict" | "user.onboarding"
    public string Tone { get; init; } = "neutral"; // "neutral" | "warm" | "formal"
    public Guid? RelatedEntityId { get; init; }    // EventId / UserId, depending on template
    public string? RelatedEntityType { get; init; }// "event" | "user"
    public DateTimeOffset RequestedAt { get; init; }
}
```

### Topology constants — `KendoTopology`

A new file to centralize queue + DLQ names so every consumer/producer agrees.

```csharp
namespace Kendo.Shared.Messaging.Topology;

public static class KendoTopology
{
    public const string UserLifecycleQueue = "kendo-events";
    public const string UserLifecycleDlq   = "kendo-events/$DeadLetterQueue";

    public const string AiFlowQueue = "kendo-events-ai";
    public const string AiFlowDlq   = "kendo-events-ai/$DeadLetterQueue";

    public const string FastApiQueue = "kendo-events-fastapi";  // reserved for future (W7 audit)
    public const string FastApiDlq   = "kendo-events-fastapi/$DeadLetterQueue";
}
```

> **Why `kendo-events-fastapi` is reserved:** W7's Worker → FastAPI summarization
> does not currently emit a downstream event, but the design leaves the option
> open. Reserving the name now prevents collision.

### `KendoRebusConfiguration` extension

The existing `AddKendoRebus` extension (Day 06) registers a single consumer on
`kendo-events`. The new `AddKendoRebusAiConsumer` registers a second consumer on
`kendo-events-ai`. The two are independent — each gets its own `NumberOfWorkers`,
its own parallelism, and its own DLQ.

```csharp
public static IServiceCollection AddKendoRebusAiConsumer(this IServiceCollection services, IConfiguration config)
{
    var conn = config["Rebus:ConnectionString"];  // same ASB, separate consumer
    if (string.IsNullOrWhiteSpace(conn))
    {
        // Local dev: skip — FastAPI workloads are gated by env anyway.
        return services;
    }

    services.AddRebus(rebus => rebus
        .Transport(t => t.UseAzureServiceBus(conn, KendoTopology.AiFlowQueue))
        .Options(o => o
            .SetNumberOfWorkers(2)
            .SetMaxParallelism(5)
            .UseKendoMessageSerializer()
            .UseKendoRebusDlq(KendoTopology.AiFlowDlq))  // Day 09 DLQ helper, extended
        .Logging(l => l.UseKendoOpenTelemetry())         // Day 04 observability
        .Routing(r => r.TypeBased()
            .Map<EventIngestedEvent>(KendoTopology.AiFlowQueue)
            .Map<EventValidatedEvent>(KendoTopology.AiFlowQueue)
            .Map<UserEmbeddingUpdatedEvent>(KendoTopology.AiFlowQueue)
            .Map<NotificationRequestedEvent>(KendoTopology.AiFlowQueue))
    );

    return services;
}
```

The Gateway and `UserService` register a **producer** (one-way client) targeting
`kendo-events-ai` via a new `AddKendoRebusAiProducer` extension that mirrors the
existing `AddKendoRebus` producer (Day 06) but points at the AI queue. Producers
share the same `KendoMessageSerializer` (Day 10) and capture `TraceContext` from
`Activity.Current` (Day 11) — no changes needed to either helper.

### `DlqDepthMonitor` extension (Worker)

The existing `DlqDepthMonitor` (Day 09) polls one DLQ at a configurable interval. The
new version registers two monitoring tasks (one per DLQ) with the same alert
threshold (`Rebus:DlqDepthThreshold`, default 5) and the same dedup logic. The monitor
emits a structured log line with `queue: "kendo-events-ai"` so chaos tests and
alerting can distinguish.

```csharp
public class DlqDepthMonitorHostedService : BackgroundService
{
    // existing ctor + ExecuteAsync unchanged, except:
    //   - reads [KendoTopology.UserLifecycleDlq, KendoTopology.AiFlowDlq]
    //   - emits one log line per (queue, depth) sample
    //   - alerts once per (queue, threshold) breach, dedup'd
}
```

### Idempotency

Day 08 established `IdempotencyRecords` keyed on `MessageId`. The four new events
inherit this behavior unchanged. Handlers (authored in `day_19_spec.md`) MUST treat
`MessageId` as the idempotency key and MUST be safe to re-deliver.

### Trace correlation (Day 11, unchanged)

Every new event inherits `TraceContext` from `KendoMessage`. The `OutboxRelayService`
(Day 10) and any direct `IBus.Send` call capture `Activity.Current?.Id` at publish
time. The Worker handlers (Day 11) extract the `traceparent` header and create a
child `Activity`. The four new events follow the same pattern — no new trace code.

---

## Implementation Plan (Commit Units)

### Unit 1 — `KendoTopology` constants
- **Files:** `src/Shared/Kendo.Shared.Messaging/Topology/KendoTopology.cs`
- **Gate:** `dotnet build src/Shared`
- **Commit:** `feat(messaging): add KendoTopology constants for queue and DLQ names`

### Unit 2 — 4 new event types
- **Files:** `src/Shared/Kendo.Shared.Messaging/Events/{EventIngested,EventValidated,UserEmbeddingUpdated,NotificationRequested}Event.cs`
- **Gate:** `dotnet build src/Shared` + `dotnet test tests/Kendo.Tests --filter "Category=Messaging"` (4 new serialization round-trip tests)
- **Commit:** `feat(messaging): add EventIngested, EventValidated, UserEmbeddingUpdated, NotificationRequested events`

### Unit 3 — `AddKendoRebusAiConsumer` extension
- **Files:** `src/Shared/Kendo.Shared.Messaging/KendoRebusAiConsumerConfiguration.cs`
- **Gate:** integration test that boots a test container, registers the consumer, and confirms `kendo-events-ai` is created with 2 workers
- **Commit:** `feat(messaging): add AddKendoRebusAiConsumer for kendo-events-ai queue`

### Unit 4 — `AddKendoRebusAiProducer` extension
- **Files:** `src/Shared/Kendo.Shared.Messaging/KendoRebusAiProducerConfiguration.cs`
- **Gate:** integration test that confirms a published `EventIngestedEvent` lands on `kendo-events-ai`
- **Commit:** `feat(messaging): add AddKendoRebusAiProducer for one-way client targeting kendo-events-ai`

### Unit 5 — `DlqDepthMonitor` multi-queue support
- **Files:** `src/Worker/Workers/DlqDepthMonitorHostedService.cs` (extended)
- **Gate:** `dotnet test tests/Kendo.Tests --filter "Category=DlqDepthMonitor"` (2 new tests: lifecycle DLQ depth, AI DLQ depth)
- **Commit:** `feat(messaging): extend DlqDepthMonitor to poll kendo-events-ai/$DeadLetterQueue`

### Unit 6 — Wire consumers/producers in service `Program.cs`
- **Files:** `src/Gateway/Program.cs`, `src/UserService/Program.cs`, `src/Worker/Program.cs`
- **Gate:** `docker compose up -d` and `docker compose exec worker dotnet ... healthcheck`; both queues visible in ASB portal
- **Commit:** `feat(messaging): wire kendo-events-ai producer on Gateway+UserService, consumer on Worker`

### Unit 7 — Docker compose + .env updates
- **Files:** `docker-compose.yml`, `.env.example` — add `KENDO__REBUS__AI__WORKERS`, `KENDO__REBUS__AI__PARALLELISM`, `KENDO__REBUS__AI__DLQ_DEPTH_THRESHOLD`
- **Gate:** `docker compose config` (config validation)
- **Commit:** `chore(env): pass AI queue env vars to gateway, userservice, worker`

---

## Success Checklist

| # | Criterion | Maps to |
|---|-----------|---------|
| 1 | `KendoTopology` exposes the 4 queue + 2 DLQ constants; existing consumers compile unchanged | M0.3 |
| 2 | The 4 new events serialize round-trip via `KendoMessageSerializer` (regression test) | M0.3 |
| 3 | `kendo-events-ai` queue is provisioned in ASB with 2 workers, parallelism 5, and `kendo-events-ai/$DeadLetterQueue` DLQ | M0.4 + CCD-4 |
| 4 | Gateway's `AddKendoRebusAiProducer` registers a one-way client targeting `kendo-events-ai` | M0.4 + CCD-4 |
| 5 | UserService's `AddKendoRebusAiProducer` registers a one-way client targeting `kendo-events-ai` | M0.4 + CCD-4 |
| 6 | Worker's `AddKendoRebusAiConsumer` registers a consumer on `kendo-events-ai` | M0.4 + CCD-4 |
| 7 | `DlqDepthMonitor` polls both DLQs and emits a structured log line per (queue, depth) sample | M0.4 + M2.4 |
| 8 | A poison message on `kendo-events-ai` does **not** affect `kendo-events` traffic (chaos test) | CCD-4 isolation guarantee |
| 9 | `traceparent` is captured at publish time and embedded in every AI event (Day 11 pattern, regression test) | Day 11 + CCD-1 |
| 10 | All existing 97 unit tests still pass (regression guard) | Inherited |
| 11 | `docker compose up` starts all 6 services; all health checks pass | Integration validation |

---

## Resilience Mandate

**Minimal — this spec is messaging-topology only.** No new HTTP endpoints. The
`AddKendoRebusAiConsumer` and `AddKendoRebusAiProducer` reuse the existing Polly
pipeline (Day 03) at the bus level, the existing `IdempotencyRecords` (Day 08) for
duplicate suppression, the existing `OutboxMessage` table (Day 10) for atomic
publishing from the UserService, and the existing `traceparent` propagation (Day
11). The DLQ path uses the existing `DeadLetterHandler` (Day 09) pattern, extended
to two queues.

The resilience policy for the **Worker → FastAPI** call (W7) is owned by
`day_19_spec.md` (Worker). The **Gateway → FastAPI** resilience is owned by
`day_17_spec.md` (Gateway). The **FastAPI-side** resilience is owned by
`fastapi_rag_service_spec.md`.

---

## Dependency Check

| Resource | Required? | Status |
|----------|-----------|--------|
| `KendoMessage` base record | Yes — provides `MessageId`, `CreatedAt`, `TraceContext` | Day 06 + Day 11; ✅ |
| `KendoMessageSerializer` | Yes — used by both consumer and producer | Day 10; ✅ |
| `Kendo.Shared.Messaging.IdempotencyRecords` | Yes — `MessageId` PK enforces uniqueness | Day 08; ✅ |
| `OutboxMessage` table | Yes — atomic publishing from `UserService` (recommended) | Day 10; ✅ |
| `OutboxRelayService` | Yes — relays events with captured `TraceContext` | Day 10 + Day 11; ✅ |
| `Event`, `EventValidation`, `UserEmbedding` entities | Yes — `EventId` references and `StructuredJson` payloads | Owned by `day_20_spec.md` |
| `Kendo.Shared.Authentication.AdminScopePolicies` | No — service JWTs are minted by Gateway, not UserService | Owned by `day_17_spec.md` |
| Gateway `IFastAPIClient` | No — producers are direct `IBus.Send` calls | Owned by `day_17_spec.md` |
| Worker handlers (`EventIngestedHandler` etc.) | No — they consume these events | Owned by `day_19_spec.md` |
| FastAPI service | No | Owned by `fastapi_rag_service_spec.md` |

---

## Open Questions / Clarifications

- **Outbox vs direct send on `EventIngestedEvent`:** the Gateway produces this
  event after W1. The Gateway does not currently use the outbox pattern (it
  publishes via direct `IBus.Send` in the existing `UsersController` flow — see
  Day 07). Confirm whether the Gateway should adopt the outbox pattern for
  `EventIngestedEvent` to match `UserService`'s atomic-publishing model, or
  whether direct send is acceptable for the Gateway (the existing
  pattern). Recommended: keep direct send on the Gateway; only `UserService`
  uses the outbox.
- **`kendo-events-fastapi` reserved name:** the topology file reserves this
  queue for a future W7 audit flow. Confirm acceptable as a placeholder, or
  remove until needed.
- **Routing for the 4 events:** the spec routes all 4 events to
  `kendo-events-ai` via `TypeBased()` mapping. Confirm whether any of them
  should be split onto a separate queue (e.g., notifications onto
  `kendo-events-fastapi` once that name is needed).
- **DLQ alert thresholds:** the existing `Rebus:DlqDepthThreshold` (default 5)
  is shared. Confirm whether the AI queue should have its own
  `KENDO__REBUS__AI__DLQ_DEPTH_THRESHOLD` (recommended) so an LLM-storm can
  trip only the AI alert.
- **Backwards compatibility for `KendoMessage`:** the new events inherit
  unchanged. Confirm no consumer in production will be broken by adding
  new event types (Rebus ignores types it has no handler for, so this
  should be safe by design).
- **Test container for ASB:** integration tests in Units 3, 4, 8 require a
  live Azure Service Bus. The existing test setup (Day 06) uses the
  `AzureServiceBus` connection string. Confirm whether the team has a
  dedicated test namespace, or whether we should use a local ASB
  emulator. (The existing tests use a real namespace; recommendation: keep
  the same.)

---

## Change Log

| Date | Author | Change |
|---|---|---|
| 2026-06-16 | Platform Architect (reconciliation) | Initial spec — Service Bus AI event contracts (M0.3) and `kendo-events-ai` queue topology (M0.4, CCD-4) |
