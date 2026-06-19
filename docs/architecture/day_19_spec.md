# Architecture Spec — Day 19 — Worker: AI Handlers

> **Milestone:** M0.5 — Worker AI handlers + direct FastAPI call (W7)  
> **Roadmap phase:** 00 — Pre-FastAPI Reconciliation (see roadmap header note)  
> **Date:** 2026-06-16  
> **Architect:** Platform Architect (Orchestrator-delegated)  
> **Depends on:** `docs/architecture/fastapi_rag_service_spec.md` § W7; consumes entities from `day_20_spec.md`; consumes events from `day_18_spec.md`; consumes the JWT trust root from `day_17_spec.md`

---

## Why this spec exists

The FastAPI spec (M5.7–M5.14) introduces 4 new domain events (`EventIngestedEvent`,
`EventValidatedEvent`, `UserEmbeddingUpdatedEvent`, `NotificationRequestedEvent`) that
need **handlers** in the Worker. The audit confirms the Worker currently has only
`UserCreatedEventHandler`, `DeadLetterHandler`, and `DlqDepthMonitor` — **no AI or
embedding-related handlers exist**. W7 (Notification Summarization) also requires the
Worker to call FastAPI **directly** (not via the Gateway), making the Worker the first
**non-Gateway caller** of the FastAPI service.

This spec authors the 4 new handlers, the Worker → FastAPI client (mirroring the
Gateway's `IFastAPIClient` from `day_17_spec.md` but registered as a Worker singleton),
and the W7 summarization call. It is the **fourth and final** reconciliatory spec and
is authored after `day_20` (entities), `day_18` (events), and `day_17` (Gateway +
JWT) because every handler references all three.

---

## Milestone Scope

- **Roadmap milestone:** M0.5 — Worker AI handlers + direct FastAPI call
- **Components touched:**
  - `src/Worker/Handlers/EventIngestedHandler.cs` — new (W1)
  - `src/Worker/Handlers/EventValidatedHandler.cs` — new (W2)
  - `src/Worker/Handlers/UserEmbeddingUpdatedHandler.cs` — new (W3/W5)
  - `src/Worker/Handlers/NotificationRequestedHandler.cs` — new (W7)
  - `src/Shared/Kendo.Shared.Http/IFastAPISummarizationClient.cs` — new (W7 subset of FastAPI client)
  - `src/Shared/Kendo.Shared.Http/FastAPISummarizationClient.cs` — new (Worker caller, mirrors `day_17` Polly)
  - `src/Worker/Workers/NotificationDispatcherHostedService.cs` — new (W7 delivery)
  - `src/Worker/Program.cs` — wires 4 new handlers + new client + `AddKendoServiceJwtMinter` (Worker reuses the Gateway's minter for direct-to-UserService calls)
  - `docker-compose.yml` — new env: `KENDO__FASTAPI__SUMMARIZATION__TIMEOUT_SECONDS`, `KENDO__WORKER__SERVICE_JWT__AUDIENCE`
  - `.env.example` — same
- **Explicitly out of scope:**
  - The `Event` / `EventValidation` / `UserEmbedding` entities (covered by `day_20_spec.md`)
  - The 4 new event types (covered by `day_18_spec.md`)
  - The Gateway's `IFastAPIClient` (covered by `day_17_spec.md` — Worker has a **separate** client with a **subset** of the surface)
  - FastAPI service code (covered by `fastapi_rag_service_spec.md`)

---

## Layer Changes

| Layer | Service | Change |
|-------|---------|--------|
| Handlers | Worker | 4 new `IHandleMessages<T>` classes |
| HTTP (client) | Kendo.Shared | `IFastAPISummarizationClient` / `FastAPISummarizationClient` for W7 |
| Delivery | Worker | `NotificationDispatcherHostedService` (consumes W7 output) |
| Configuration | docker-compose | New env: `KENDO__FASTAPI__SUMMARIZATION__...`, `KENDO__WORKER__SERVICE_JWT__AUDIENCE` |

---

## Data Contracts

### Common base — `KendoMessage` (unchanged)

Inherited unchanged. All 4 handlers implement `IHandleMessages<TMessage>` where
`TMessage` is one of the 4 events authored in `day_18_spec.md`.

### Handler: `EventIngestedHandler` (W1)

Consumes `EventIngestedEvent` (emitted by the Gateway after W1 returns structured
JSON). Writes the `Event` row to `UserService`, persists the embedding, and emits
the follow-on `EventValidatedEvent` for W2.

```csharp
public class EventIngestedHandler : IHandleMessages<EventIngestedEvent>
{
    public async Task Handle(EventIngestedEvent message, IMessageContext context)
    {
        // 1. Idempotency check (Day 08 — IdempotencyRecords)
        if (await _idempotency.IsProcessed(message.MessageId)) return;

        // 2. Validate StructuredJson against the Event schema
        var structured = JsonSerializer.Deserialize<EventDto>(message.StructuredJson)
            ?? throw new InvalidOperationException("StructuredJson is not valid Event JSON");

        // 3. POST to UserService via IFastAPIClient (the Worker uses a
        //    separate IFastAPIClient instance — see Resilience Mandate)
        await _userServiceClient.UpsertEventAsync(structured, ct);

        // 4. POST the embedding to UserService's EmbeddingAdminController
        //    using a service JWT (CCD-3, day_20)
        var adminToken = _serviceJwtMinter.MintAdminWritesToken();
        await _embeddingAdminClient.UpsertEmbeddingAsync(
            target: "event",
            targetId: message.EventId,
            modelName: message.EmbeddingModel,
            dimensions: message.EmbeddingDimensions,
            embedding: message.Embedding,
            bearerToken: adminToken,
            ct: ct);

        // 5. Emit EventValidatedEvent to kendo-events-ai for W2 fanout
        await _bus.Send(new EventValidatedEvent
        {
            EventId = message.EventId,
            CorrelationId = message.CorrelationId,
            // W2 will fill these in via the FastAPI /v1/events/{id}/validate call
            Ok = false, ConflictsJson = "[]", SuggestionsJson = "[]",
            ValidatedAt = default  // will be set by W2's handler
        });

        // 6. Mark idempotency
        await _idempotency.MarkProcessed(message.MessageId);
    }
}
```

> **Idempotency contract:** re-delivery of an `EventIngestedEvent` is a no-op
> because (a) the `IdempotencyRecords.MessageId` check short-circuits, and
> (b) the `Event` row upsert is keyed on `EventId` and the embedding write is
> keyed on `event_embeddings.EventId`. Re-running produces no duplicates.

> **Why the Worker calls `UserService` directly, not via the Gateway:** the
> Worker is **inside** the cluster and has a service JWT. Going through the
> Gateway would add an extra hop, an extra JWT validation, and a second
> circuit breaker — none of which we want for an internal write. The Worker
> has a direct `HttpClient` to `UserService` (this is the first such direct
> service-to-service call, but the pattern is the same Polly shape the
> Gateway's `IFastAPIClient` uses — see Resilience Mandate).

### Handler: `EventValidatedHandler` (W2)

Consumes `EventValidatedEvent` (emitted by `EventIngestedHandler`). Calls FastAPI
W2 (`/v1/events/{id}/validate`) to get the validation result, persists the
`EventValidation` row, and may emit a `NotificationRequestedEvent` for W7.

```csharp
public class EventValidatedHandler : IHandleMessages<EventValidatedEvent>
{
    public async Task Handle(EventValidatedEvent message, IMessageContext context)
    {
        if (await _idempotency.IsProcessed(message.MessageId)) return;

        // 1. Call FastAPI W2 to get the validation result
        var result = await _fastApi.ValidateEventAsync(
            message.EventId,
            new EventValidationRequest { /* fetch candidate from UserService */ },
            ct);

        // 2. PATCH the EventValidation row via UserService
        await _userServiceClient.UpdateEventValidationAsync(
            message.EventId,
            new EventValidationDto
            {
                Ok = result.Ok,
                ConflictsJson = result.ConflictsJson,
                SuggestionsJson = result.SuggestionsJson,
                ReasoningTrace = result.ReasoningTrace
            },
            ct);

        // 3. If validation failed (or succeeded but interesting), emit
        //    a NotificationRequestedEvent for W7. Otherwise, no-op.
        if (!result.Ok || result.Conflicts.Count > 0)
        {
            await _bus.Send(new NotificationRequestedEvent
            {
                UserId = /* fetch from Event */,
                TemplateId = result.Ok ? "event.conflict" : "event.rejected",
                Tone = "neutral",
                RelatedEntityId = message.EventId,
                RelatedEntityType = "event",
                RequestedAt = DateTimeOffset.UtcNow
            });
        }

        await _idempotency.MarkProcessed(message.MessageId);
    }
}
```

### Handler: `UserEmbeddingUpdatedHandler` (W3, W5)

Consumes `UserEmbeddingUpdatedEvent` (emitted by `UserService` after a successful
reindex or first-embed). Emits a welcome notification for first embeds, audit-logs
reindexes, and is otherwise a no-op.

```csharp
public class UserEmbeddingUpdatedHandler : IHandleMessages<UserEmbeddingUpdatedEvent>
{
    public async Task Handle(UserEmbeddingUpdatedEvent message, IMessageContext context)
    {
        if (await _idempotency.IsProcessed(message.MessageId)) return;

        switch (message.Trigger)
        {
            case "first_embed":
                await _bus.Send(new NotificationRequestedEvent
                {
                    UserId = message.UserId,
                    TemplateId = "user.profile_indexed",
                    Tone = "warm",
                    RelatedEntityId = message.UserId,
                    RelatedEntityType = "user",
                    RequestedAt = DateTimeOffset.UtcNow
                });
                break;

            case "reindex":
            case "model_swap":
                _logger.LogInformation("Embedding updated for user {UserId} via {Trigger}",
                    message.UserId, message.Trigger);
                break;
        }

        await _idempotency.MarkProcessed(message.MessageId);
    }
}
```

### Handler: `NotificationRequestedHandler` (W7)

Consumes `NotificationRequestedEvent`. Calls FastAPI's W7 summarization endpoint
(`/v1/notifications/summarize`) over SSE, hands the rendered body to the
`NotificationDispatcherHostedService`, and emits a follow-up
`NotificationDispatchedEvent` (the downstream contract for that event is not in
this spec — it's a future notification-pipeline spec, or the existing
notification contract if one already exists).

```csharp
public class NotificationRequestedHandler : IHandleMessages<NotificationRequestedEvent>
{
    public async Task Handle(NotificationRequestedEvent message, IMessageContext context)
    {
        if (await _idempotency.IsProcessed(message.MessageId)) return;

        // 1. Call FastAPI W7 (SSE) and collect the rendered notification body
        var rendered = new StringBuilder();
        await foreach (var chunk in _fastApiSummarization.SummarizeStreamAsync(
            new SummarizationRequest
            {
                EventId = message.RelatedEntityType == "event" ? message.RelatedEntityId : null,
                UserId = message.UserId,
                TemplateId = message.TemplateId,
                Tone = message.Tone
            },
            ct))
        {
            rendered.Append(chunk.Text);
        }

        // 2. Hand the rendered body to the delivery service
        await _dispatcher.EnqueueAsync(new NotificationDispatchJob
        {
            UserId = message.UserId,
            TemplateId = message.TemplateId,
            RenderedBody = rendered.ToString(),
            PromptVersion = "v1",   // populated from the FastAPI response
            RequestedAt = message.RequestedAt
        }, ct);

        await _idempotency.MarkProcessed(message.MessageId);
    }
}
```

> **Why SSE for W7:** the FastAPI spec §W7 specifies SSE for notification
> summarization. The Worker collects the stream into a single string rather than
> forwarding the SSE events to the user (the user is not in the request path
> here — the notification is delivered asynchronously by the dispatcher).

### `NotificationDispatcherHostedService`

A `BackgroundService` that consumes `NotificationDispatchJob` items from an
in-memory `Channel<NotificationDispatchJob>` and hands them to whatever delivery
channel exists (email, push, in-app). The dispatch contract is out of scope for
this spec — the hosted service is the seam, and the actual delivery is a future
notification-pipeline spec.

> **Why an in-memory channel and not a queue:** notifications are
> ephemeral; if the Worker crashes mid-delivery, the next instance rebuilds
> the channel from the `NotificationRequestedEvent` flow on Rebus. A durable
> queue would be overkill for a feature the platform does not yet have
> (no email provider, no push channel).

### `IFastAPISummarizationClient` (W7 subset)

The Worker uses a **subset** of the Gateway's `IFastAPIClient` surface — only the
W7 method. This is a separate interface so the Worker doesn't pull in the W1/W2
DTOs.

```csharp
public interface IFastAPISummarizationClient
{
    IAsyncEnumerable<SummarizationStreamChunk> SummarizeStreamAsync(
        SummarizationRequest request, CancellationToken ct);
}

public class SummarizationStreamChunk
{
    public string Text { get; init; } = "";
    public string PromptVersion { get; init; } = "v1";
}

public class SummarizationRequest
{
    public Guid? EventId { get; init; }
    public Guid UserId { get; init; }
    public string TemplateId { get; init; } = "";
    public string Tone { get; init; } = "neutral";
}
```

`FastAPISummarizationClient` mirrors the Gateway's `FastAPIClient` (Polly
timeout + retry + circuit breaker) with **W7-specific** defaults: shorter
timeout (15s, since W7 is a one-shot summarization), independent circuit
breaker (so a slow W7 doesn't trip the W1/W2 breaker).

> **Why a separate client and not a shared `IFastAPIClient`:** the Worker does
> not call W1, W2, W3, W4, or W6. Sharing the Gateway's interface would force
> the Worker to carry W1–W6 DTOs it never uses. The two clients are independent
> singletons with independent Polly policies and independent circuit breakers,
> even though they target the same FastAPI host.

---

## Implementation Plan (Commit Units)

### Unit 1 — `IFastAPISummarizationClient` + `FastAPISummarizationClient`
- **Files:** `src/Shared/Kendo.Shared.Http/{IFastAPISummarizationClient,FastAPISummarizationClient,SummarizationRequest,SummarizationStreamChunk}.cs`
- **Gate:** unit tests for the Polly policy (timeout, retry, circuit breaker) and for SSE chunk parsing
- **Commit:** `feat(http): add IFastAPISummarizationClient with Polly and SSE chunk parsing (W7)`

### Unit 2 — `EventIngestedHandler`
- **Files:** `src/Worker/Handlers/EventIngestedHandler.cs`
- **Gate:** integration test: publish `EventIngestedEvent` → confirm `Event` row exists in UserService, embedding exists, `EventValidatedEvent` is published to `kendo-events-ai`; re-delivery is a no-op
- **Commit:** `feat(worker): add EventIngestedHandler for W1 (Event Ingestion fanout)`

### Unit 3 — `EventValidatedHandler`
- **Files:** `src/Worker/Handlers/EventValidatedHandler.cs`
- **Gate:** integration test: publish `EventValidatedEvent` with W2 mock returning a conflict → confirm `EventValidation` row updated, `NotificationRequestedEvent` published; no conflict → no notification
- **Commit:** `feat(worker): add EventValidatedHandler for W2 (Event Validation fanout)`

### Unit 4 — `UserEmbeddingUpdatedHandler`
- **Files:** `src/Worker/Handlers/UserEmbeddingUpdatedHandler.cs`
- **Gate:** unit tests for the 3 trigger branches (first_embed → notification, reindex → log, model_swap → log)
- **Commit:** `feat(worker): add UserEmbeddingUpdatedHandler for W3/W5 (User Embedding audit + welcome)`

### Unit 5 — `NotificationRequestedHandler` + `NotificationDispatcherHostedService`
- **Files:** `src/Worker/Handlers/NotificationRequestedHandler.cs`, `src/Worker/Workers/NotificationDispatcherHostedService.cs`
- **Gate:** integration test: publish `NotificationRequestedEvent` → confirm FastAPI W7 is called, dispatcher receives the rendered body
- **Commit:** `feat(worker): add NotificationRequestedHandler and NotificationDispatcherHostedService (W7)`

### Unit 6 — Wire handlers, clients, dispatcher in `Program.cs`
- **Files:** `src/Worker/Program.cs` — registers `AddKendoRebusAiConsumer` (from day_18), the 4 handlers, `AddKendoFastApiSummarizationClient`, `AddKendoServiceJwtMinter`, `NotificationDispatcherHostedService`
- **Gate:** `dotnet build src/Worker`; `dotnet test`; `docker compose up -d worker`; end-to-end test that publishes `EventIngestedEvent` and watches the Event row appear
- **Commit:** `feat(worker): wire AI handlers, FastAPI summarization client, service-JWT minter, notification dispatcher`

### Unit 7 — Docker compose + .env updates
- **Files:** `docker-compose.yml`, `.env.example` — add `KENDO__FASTAPI__SUMMARIZATION__TIMEOUT_SECONDS`, `KENDO__WORKER__SERVICE_JWT__AUDIENCE`
- **Gate:** `docker compose config` (validation)
- **Commit:** `chore(env): pass FastAPI summarization and service-JWT env vars to worker`

---

## Success Checklist

| # | Criterion | Maps to |
|---|-----------|---------|
| 1 | `EventIngestedHandler` writes the `Event` row + embedding and publishes `EventValidatedEvent`; re-delivery is a no-op | M0.5 + W1 |
| 2 | `EventValidatedHandler` calls FastAPI W2, persists the `EventValidation` row, and emits `NotificationRequestedEvent` on conflict | M0.5 + W2 |
| 3 | `UserEmbeddingUpdatedHandler` emits a welcome notification for first embeds and audit-logs reindex/model_swap | M0.5 + W3 + W5 |
| 4 | `NotificationRequestedHandler` consumes SSE from FastAPI W7 and hands the rendered body to `NotificationDispatcherHostedService` | M0.5 + W7 |
| 5 | `IFastAPISummarizationClient` uses an independent Polly pipeline (15s timeout, its own circuit breaker) so W7 cannot starve W1/W2 | M0.5 + M5.5 |
| 6 | `IFastAPISummarizationClient` maps FastAPI's RFC 7807 responses to the Worker's existing `KendoProblemDetails` | M0.5 |
| 7 | The Worker can call `UserService`'s `EmbeddingAdminController` using a service JWT minted by `IServiceJwtMinter` (CCD-3) | M0.5 + CCD-3 |
| 8 | All 4 new handlers register idempotency on `MessageId` (Day 08 pattern) | Day 08 + M0.5 |
| 9 | All 4 new handlers extract `traceparent` from the message header and create a child `Activity` (Day 11 pattern) | Day 11 + M0.5 |
| 10 | `NotificationDispatcherHostedService` consumes from an in-memory `Channel<NotificationDispatchJob>`; channel rebuilds on Worker restart | M0.5 |
| 11 | All existing 97 unit tests still pass (regression guard) | Inherited |
| 12 | `docker compose up` starts all 6 services; end-to-end test (publish `EventIngestedEvent` → `Event` row visible in `kendo_users`) | Integration validation |

---

## Resilience Mandate

The Worker is the **first non-Gateway caller** of FastAPI and the **first
service-to-service** caller of `UserService` for AI flows. This spec establishes
the resilience pattern that future cross-service calls will follow.

### Worker → FastAPI (W7)

| Concern | Implementation |
|---|---|
| Timeout | Polly `TimeoutPolicy(15s)` — W7-specific, shorter than the Gateway's 30s |
| Retry | Polly `RetryPolicy(3 attempts, exponential + jitter, transient-fault predicate)` |
| Circuit breaker | Polly `CircuitBreakerPolicy(3 failures → 30s break)` — **independent** of the Gateway's W1/W2 breaker |
| RFC 7807 | Worker's `FastAPISummarizationClient` maps FastAPI's `application/problem+json` to the Worker's existing `KendoProblemDetails` (reuses the Day 05 pattern) |
| Observability | OpenTelemetry `ActivitySource` with `kind=client`, `peer=fastapi:8000`, `operation=summarize` |

### Worker → UserService (W1, W2, W3/W5 — direct calls)

| Concern | Implementation |
|---|---|
| Timeout | Polly `TimeoutPolicy(5s)` — internal call, expected fast |
| Retry | Polly `RetryPolicy(2 attempts, exponential, transient-fault only)` — fewer retries than external calls |
| Circuit breaker | Polly `CircuitBreakerPolicy(5 failures → 15s break)` — looser threshold because `UserService` is internal |
| Auth | Service JWT minted by `IServiceJwtMinter` (from `day_17_spec.md`) with `scope: "admin:writes"`, `token_use: "service"` |
| RFC 7807 | `UserServiceClient` maps the existing `KendoProblemDetails` 1:1 (no transformation needed) |
| Observability | OpenTelemetry `ActivitySource` with `kind=client`, `peer=userservice:5001`, `operation=upsert_event` |

### Per-handler resilience

- **Idempotency:** `IdempotencyRecords` (Day 08) — `MessageId` PK enforces uniqueness; re-delivery is a no-op
- **Crash recovery:** the `OutboxRelayService` (Day 10) and the existing `IdempotencyRecords` Processing/Completed state machine ensure crash-mid-handler is recoverable
- **DLQ on poison message:** the `DeadLetterHandler` (Day 09) catches unhandled exceptions; the message lands on `kendo-events-ai/$DeadLetterQueue` and triggers the `DlqDepthMonitor` alert (extended in `day_18_spec.md` to cover the AI queue)

> **Why the Worker → UserService call uses a service JWT, not a user JWT:** the
> Worker's AI flow is internal — there is no end-user context. The `admin:writes`
> scope is the only scope that grants write access to `events` and
> `event_embeddings` (per `day_20_spec.md`). The `token_use=service` claim
> prevents any user JWT from being misconfigured to grant the same scope.

---

## Dependency Check

| Resource | Required? | Status |
|----------|-----------|--------|
| `KendoMessage` base record | Yes — `MessageId`, `CreatedAt`, `TraceContext` | Day 06 + Day 11; ✅ |
| `IdempotencyRecords` | Yes — every handler checks/marks idempotency | Day 08; ✅ |
| `Kendo.Shared.Observability` | Yes — handlers create child `Activity` | Day 04; ✅ |
| `KendoProblemDetails` | Yes — RFC 7807 error responses | Day 05; ✅ |
| `Polly` v8 | Yes — resilience pipeline | Day 03; ✅ |
| `Kendo.Shared.Resilience` | Yes — same Polly shape as the Gateway's `FastAPIClient` | Day 03; ✅ |
| `Event`, `EventValidation`, `UserEmbedding` entities | Yes — handler DTOs reference them | Owned by `day_20_spec.md` |
| `EventIngestedEvent`, `EventValidatedEvent`, `UserEmbeddingUpdatedEvent`, `NotificationRequestedEvent` | Yes — handler `TMessage` parameters | Owned by `day_18_spec.md` |
| `AddKendoRebusAiConsumer` | Yes — Worker registers the consumer | Owned by `day_18_spec.md` |
| `Kendo.Shared.Authentication.IServiceJwtMinter` | Yes — Worker mints service JWTs for `UserService` `EmbeddingAdminController` calls | Owned by `day_17_spec.md` |
| `Kendo.Shared.Authentication.AdminScopePolicies` | No — Worker does not validate `admin:writes`; it presents the scope | Owned by `day_20_spec.md` + `day_17_spec.md` |
| `IFastAPIClient` (Gateway) | No — Worker has its own `IFastAPISummarizationClient` | Owned by `day_17_spec.md` |
| FastAPI service | No — endpoint contracts only | Owned by `fastapi_rag_service_spec.md` |

---

## Open Questions / Clarifications

- **Notification delivery contract:** the `NotificationDispatchedEvent` emitted
  by `NotificationDispatcherHostedService` is not specified here because there
  is no existing notification pipeline in the project. Confirm whether this
  spec should author the full delivery contract (email, push, in-app) or
  whether a future spec owns it. Recommended: leave the dispatcher as a
  seam and let the next spec own delivery.
- **Worker → UserService direct call:** this is the first **direct**
  service-to-service call in the platform. The existing pattern (Day 01) is
  Gateway → internal service. Confirm whether direct Worker → UserService is
  acceptable, or whether all calls should still go through the Gateway. The
  spec assumes direct is acceptable because (a) the Worker has a service JWT
  and (b) an extra Gateway hop would add latency to a hot path.
- **`IFastAPISummarizationClient` timeout:** the spec uses 15s. Confirm
  whether W7's p95 should be tighter (the FastAPI spec §W7 says p95 ≤ 6s
  end-to-end, which includes dispatcher + delivery, not just the LLM call;
  15s leaves 9s for delivery).
- **W3 — `UserEmbeddingUpdatedEvent` source:** the spec says UserService
  emits this event after a successful embed (W3 first-embed, W5 reindex).
  Confirm whether UserService should emit via outbox (Day 10) or direct
  send. Recommended: outbox, to match the `UserCreatedEvent` pattern.
- **Per-handler chaos tests:** the spec does not enumerate per-handler chaos
  scenarios. The M3.3 chaos suite already covers DB-down, service-crash, and
  network-partition; W7-specific chaos (FastAPI slow, FastAPI returning
  invalid SSE, FastAPI returning non-RFC-7807 errors) should be added in a
  future spec. Confirm acceptable to defer.
- **Idempotency on the Worker → UserService call:** the spec relies on
  `IdempotencyRecords.MessageId` for the handler's idempotency, but the
  `UserService` `POST /api/events` is **not** idempotent on its own (it
  always creates a new row). Confirm whether `UserService` should accept
  an `Idempotency-Key` header on `POST /api/events` to make the call
  idempotent end-to-end. Recommended: yes, add the header in this spec.

---

## Change Log

| Date | Author | Change |
|---|---|---|
| 2026-06-16 | Platform Architect (reconciliation) | Initial spec — Worker AI handlers (M0.5) covering W1, W2, W3/W5, W7; first non-Gateway FastAPI caller; first direct Worker → UserService call |
