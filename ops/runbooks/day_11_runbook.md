# Day 11 — Traceparent Propagation Runbook
> **Milestone:** M2.6 — Async observability: `traceparent` propagated across producer and consumer; correlated spans visible in traces.  
> **Service:** UserService (producer) + Worker (consumer)  
> **Last updated:** 2026-06-13

---

## Overview

This milestone adds end-to-end trace correlation across the outbox relay's temporal boundary. When a user registers via `POST /api/users`, the HTTP request's OpenTelemetry `Activity` produces a W3C traceparent string. That string is stored in the `OutboxMessage.TraceContext` column at write time, forwarded as a Rebus message header (`traceparent`) by `OutboxRelayService`, and extracted by `UserCreatedEventHandler` to create a child `Activity` on the consumer side.

Both producer and consumer spans share the same trace ID — visible in OpenTelemetry console exporter output and correlated across all `ILogger` log lines.

---

## Trace Flow

```
HTTP POST /api/users                        [kendo-userservice]
  └─ Activity: CreateUserRequest            trace ID = T, span ID = A
       └─ OutboxRepository.AddAsync         captures traceparent = 00-T-A-01
                                                     
[OutboxRelayService publishes later]         [kendo-userservice]
  └─ bus.Send(headers: { "traceparent": "00-T-A-01" })
       │
       ▼  Azure Service Bus ── message ──▶    
                                           
UserCreatedEventHandler.Handle              [kendo-worker]
  └─ Activity: UserCreatedEvent.Process     trace ID = T (same!), parent = A
     ├─ Idempotency check
     ├─ Database operations
     └─ Status update
```

### What to look for in traces

Console exporter output (both services):
```
kendo-userservice: Activity.TraceId: T, Activity.SpanId: A
kendo-worker:      Activity.TraceId: T, Activity.SpanId: B, Activity.ParentSpanId: A
```

---

## Data Changes

### New Column: `OutboxMessage.TraceContext`

| Column | Type | Nullable | Description |
|---|---|---|---|
| `TraceContext` | `text` | YES | W3C traceparent string (e.g. `00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01`) |

- No backfill needed — existing rows remain NULL
- No index needed — metadata column, not a query filter
- Migration: `AddTraceContext` (idempotent)

### Rebus Header Contract

| Header Key | Present When | Consumer Action |
|---|---|---|
| `traceparent` | Ambient Activity existed at outbox-write time | Creates child Activity linked to producer trace |

---

## Configuration

No new configuration variables. The traceparent capture is automatic — no opt-in required. The consumer creates a fresh trace if no header is present (backward-compatible with existing messages).

---

## Health & Monitoring

### Log Patterns

| Log Level | Pattern | Meaning |
|---|---|---|
| `Information` | `Trace correlation: traceId={TraceId}, parentSpanId={ParentSpanId}` | Producer trace linked to consumer activity |
| `Information` | `Processing UserCreatedEvent: MessageId={Id}...` | Normal handler processing (includes trace ID via OpenTelemetry ILogger integration) |

### Verifying Trace Correlation in Dev

1. Register a user: `curl -X POST http://localhost:5001/api/users -H 'Content-Type: application/json' -d '{"email":"trace@test.com","displayName":"Trace Test"}'`
2. Check UserService logs for the HTTP request Activity:
   ```
   Activity.TraceId: T, Activity.SpanId: A
   ```
3. Check Worker logs for the handler Activity (appears within ~5s):
   ```
   Activity.TraceId: T (same T!), Activity.ParentSpanId: A
   ```

If the trace IDs match and `ParentSpanId` is set, correlation is working.

---

## Recovery Procedures

### 1. Missing traceparent headers

**Symptoms:** Worker logs show no `ParentSpanId` — consumer spans create fresh traces.

**Causes:**
- Outbox records written before this migration (no `TraceContext`) — expected, no backfill needed
- No ambient Activity at outbox-write time (e.g., relay triggered by non-HTTP code path) — expected, best-effort correlation
- Activity.Current unexpectedly null — log at Debug level would reveal (not currently logged)

**Resolution:** No action needed. Correlation is best-effort. Messages without traceparent still process normally — only the trace link metadata is lost.

### 2. Malformed traceparent parsing

**Symptoms:** Warning log from `StartTraceActivity` catch block (not currently emitted — silent catch).

**Behavior:** The handler catches all exceptions from `Activity.SetParentId()` and falls back to a fresh trace. The business logic continues normally — no message loss, no DLQ, no retry.

**Debugging:** If you suspect malformed headers, add a temporary Debug log inside the catch block to capture the raw traceparent value.

---

## Rollback

1. Revert the `UserCreatedEventHandler.StartTraceActivity` call in the handler
2. Remove the headers dictionary from `OutboxRelayService.ProcessBatchAsync`
3. Revert `OutboxRepository.AddAsync` TraceContext capture
4. Drop the `TraceContext` column migration: `dotnet ef migrations remove --project src/UserService`
5. (Optional) Keep the column — NULL values are harmless and backward-compatible

Rolling back trace correlation does not affect message delivery or idempotency. The outbox pattern continues to work normally.
