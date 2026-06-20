# Architecture Spec — Day 34 — W7: Event Notification Summarization

> **Milestone:** M5.13 — W7 Event Notification Summarization (P2)  
> **Roadmap phase:** 05 — AI/Vector Service (FastAPI)  
> **Date:** 2026-06-20  
> **Type:** Workload spec (adopts by reference `fastapi_rag_service_spec.md §W7`)  
> **Depends on:** `docs/architecture/fastapi_rag_service_spec.md` §W7 (design-spec contract, rev 2026-06-16), `docs/architecture/day_19_spec.md` (Worker AI handlers + IFastAPISummarizationClient)

---

## Milestone Scope

- **What:** Implement W7 — Worker calls FastAPI directly (not through Gateway) to generate personalized event notification summaries. FastAPI SSE streams back the rendered notification body. Worker delivers via existing channel (email/push/in-app).
- **Maps to:** `fastapi_rag_service_spec.md §W7 — Event Notification Summarization` (data contracts, acceptance gates)
- **Explicitly out of scope:**
  - W6 (already live from Day 33)
  - Notification delivery channel (Worker handles this)
  - Replacing existing templated notifications — W7 adds LLM-personalized summaries alongside templates

---

## Layer Changes

| Layer | Service | Change |
|-------|---------|--------|
| Application | `src/FastAPIService/app/api/v1/notifications.py` | New — `POST /v1/notifications/summarize` SSE endpoint |
| Application | `src/FastAPIService/app/rag/notification_chain.py` | New — prompt-template chain with tone control, prompt-version tracking |
| Application | `src/Worker/Services/NotificationSummarizationClient.cs` | New — `INotificationSummarizationClient` in `Kendo.Shared` (first non-Gateway FastAPI caller) |
| Application | `src/Worker/Handlers/EventCreatedHandler.cs` | Modify — call FastAPI for summarization before dispatching |
| Application | `src/Kendo.Shared/Services/INotificationSummarizationClient.cs` | New — interface in Shared library |
| Application | `src/Kendo.Shared/Services/NotificationSummarizationClient.cs` | New — implementation with independent Polly pipeline |

---

## Data Contracts

Adopted by reference from `fastapi_rag_service_spec.md §W7 — Event Notification Summarization` (rev 2026-06-16).

**Worker → FastAPI (direct, not through Gateway):**
```json
POST /v1/notifications/summarize (SSE)
{
  "event_id": "uuid",
  "user_id": "uuid",
  "template_id": "string",
  "tone": "professional" | "friendly" | "urgent"
}
```

**FastAPI → Worker (SSE stream):**
```
event: chunk
data: {"text": "Hello ", "token_count": 1}

event: chunk
data: {"text": "Bob, ", "token_count": 2}

event: done
data: {
  "notification_body": "Hello Bob, your event 'Birthday Party' is confirmed...",
  "prompt_version": "w7-notification-v3",
  "total_tokens": 128,
  "trace_id": "string"
}
```

**On error:**
```
event: error
data: {
  "type": "about:blank",
  "title": "Summarization Failed",
  "status": 503,
  "detail": "Azure OpenAI circuit breaker open",
  "trace_id": "string"
}
```

---

## Implementation Plan (Commit Units)

### Unit 1 — FastAPI notification summarization endpoint (SSE)

**Files:**
- `src/FastAPIService/app/api/v1/notifications.py` — `POST /v1/notifications/summarize` SSE endpoint
- `src/FastAPIService/app/rag/notification_chain.py` — prompt-template chain with tone control, prompt-version header, streaming output parser
- `tests/fastapi/test_notifications.py` — tone-control, prompt-version, streaming, error tests

**Gate command:** `pytest tests/fastapi/test_notifications.py` — must exit 0 with per-tenant tone control verified.

**Commit message:**
```
feat(fastapi): add W7 notification summarization SSE endpoint

POST /v1/notifications/summarize streams personalized notification body via SSE
with per-tenant tone control (professional/friendly/urgent). Prompt version stored
on each response for audit trail. RFC 7807 on failure. Implements
fastapi_rag_service_spec.md §W7.

Day 34 — M5.13 Unit 1 of 3 | Milestone: M5.13 — W7 Event Notification Summarization
Coverage: 100% tone-control + prompt-version tests
Lint: clean
```

### Unit 2 — Kendo.Shared NotificationSummarizationClient (first non-Gateway caller)

**Files:**
- `src/Kendo.Shared/Services/INotificationSummarizationClient.cs` — new interface
- `src/Kendo.Shared/Services/NotificationSummarizationClient.cs` — implementation with independent Polly pipeline (30s timeout, 3 retry, 3-failure → 30s CB)
- `src/Kendo.Shared/DI/AddKendoNotificationSummarization.cs` — DI registration extension method
- `tests/Kendo.Tests/Shared/NotificationSummarizationClientTests.cs` — unit tests

**Gate command:** `dotnet test tests/Kendo.Tests --filter "Category=Notification"` — must exit 0.

**Commit message:**
```
feat(shared): add NotificationSummarizationClient — first non-Gateway FastAPI caller

New INotificationSummarizationClient interface in Kendo.Shared with independent
Polly pipeline (30s timeout, 3 retry, 3-failure/30s CB). Uses service-JWT minted
via Gateway's token endpoint (Day 26/CF-2). First external caller of FastAPI that
does NOT route through Gateway.

Day 34 — M5.13 Unit 2 of 3 | Milestone: M5.13 — W7 Event Notification Summarization
Coverage: 100% unit tests
Lint: clean
```

### Unit 3 — Worker integration: call FastAPI before notification dispatch

**Files:**
- `src/Worker/Handlers/EventCreatedHandler.cs` — modify: on event creation, call `INotificationSummarizationClient` before dispatching notification
- `src/Worker/Handlers/EventCreatedNotificationDispatch.cs` — new: extracted notification dispatch logic (if refactor needed)
- `tests/Worker.Tests/NotificationSummarizationTests.cs` — integration test

**Gate command:** `dotnet test tests/Worker.Tests --filter "Category=Notification"` — must exit 0.

**Commit message:**
```
feat(worker): integrate W7 notification summarization into EventCreatedHandler

Worker calls FastAPI via NotificationSummarizationClient before dispatching
notification. On FastAPI failure (RFC 7807), Worker DLQs the message rather than
retry-looping. On SSE stream error mid-generation, partial body is discarded
and Worker falls back to template-based notification.

Day 34 — M5.13 Unit 3 of 3 | Milestone: M5.13 — W7 Event Notification Summarization
Coverage: 100% integration tests (happy + failure paths)
Lint: clean
```

---

## Success Checklist

Maps 1:1 to `fastapi_rag_service_spec.md §W7 acceptance gate`:

| # | Criterion | Maps to |
|---|-----------|---------|
| 1 | End-to-end notification latency p95 ≤ 6s (Worker → FastAPI → SSE complete → Worker delivers) | M5.13 acceptance |
| 2 | Per-tenant tone control respected (professional / friendly / urgent) | M5.13 acceptance |
| 3 | Prompt version stored on the produced notification for audit | M5.13 acceptance |
| 4 | Full RFC 7807 on failure — Worker DLQs rather than retry-loops | M5.13 acceptance |
| 5 | NotificationSummarizationClient has independent Polly pipeline (does not share Gateway's breaker capacity) | Resilience design |
| 6 | On SSE stream error mid-generation: Worker falls back to template, logs warning, does not DLQ | Resilience design |

---

## Resilience Mandate

- **Independent Polly pipeline:** W7's `NotificationSummarizationClient` must NOT share capacity with the Gateway's `IFastAPIClient`. Each has its own circuit breaker state. This design is already established in Day 19 (Worker's `IFastAPISummarizationClient` was the precedent) — W7 follows the same pattern.
- **Worker-side behavior:** On FastAPI RFC 7807 error → DLQ the message (not retry-loop). On SSE stream aborted mid-generation → discard partial body, fall back to templated notification, log warning, complete normally (not DLQ).
- **No direct Gateway dependency:** Worker calls FastAPI directly on the internal network. No Gateway hop = no shared circuit breaker contention.

---

## Depends on

- `docs/architecture/fastapi_rag_service_spec.md §W7` — data contracts, acceptance gates
- `docs/architecture/day_19_spec.md` — Worker AI handlers precedent (IFastAPISummarizationClient pattern)
- Day 26 (CF-2) — service-JWT issuance available for Worker → FastAPI calls
- `src/Kendo.Shared/` — new interface + implementation
- `src/Worker/Handlers/EventCreatedHandler.cs` — existing handler, modified
