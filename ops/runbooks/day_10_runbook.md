# Day 10 — Outbox Pattern Runbook
> **Milestone:** M2.5 — Transactional Outbox  
> **Service:** UserService (producer-side only)  
> **Last updated:** 2026-06-13

---

## Overview

The transactional outbox pattern ensures **atomic publish** of domain events from the UserService. Instead of publishing events directly via Rebus `IBus.Send()` in the controller, events are first written to an `OutboxMessages` table in the same database transaction as the business operation. A background relay (`OutboxRelayService`) polls this table and publishes pending messages.

This guarantees that a message is never published without a corresponding database write, and a database write never occurs without a corresponding outbox entry — eliminating the dual-write problem.

---

## Architecture

```
POST /api/users ──▶ [Transaction: create User + INSERT OutboxMessage]
                                                          │
                                              OutboxMessages table
                                              (ProcessedAt = NULL)
                                                          │
                                              OutboxRelayService
                                              (polls every 5s)
                                                          │
                                              Rebus IBus.Send()
                                                          │
                                              kendo-events (ASB)
                                                          │
                                              Worker consumer (idempotent)
```

### Key Tables

| Table | Database | Purpose |
|---|---|---|
| `OutboxMessages` | `kendo_users` (UserService) | Pending event storage |
| `Users` | `kendo_users` (UserService) | User data (same DB — ensures transaction scope) |

---

## Configuration

### Environment Variables

| Variable | Default | Description |
|---|---|---|
| `Relays__Outbox__PollingIntervalSeconds` | `5` | How often the relay polls for pending messages |
| `Relays__Outbox__BatchSize` | `20` | Max messages processed per poll cycle |
| `Relays__Outbox__MaxRetries` | `5` | Max delivery attempts before permanent skip |

### Rollout Recommendation

- **Pre-production:** `PollingIntervalSeconds: 5`, `BatchSize: 20` (default)
- **High-volume production:** Lower interval to `2`, increase batch size to `100` — monitor DB connection pool pressure
- **Post-deploy spike:** If outbox grows faster than relay can drain, tune `BatchSize` and `PollingIntervalSeconds` first before scaling relay replicas

---

## Health & Monitoring

### Dashboard Query

```sql
-- Current outbox depth (pending messages)
SELECT COUNT(*) AS pending_count
FROM "OutboxMessages"
WHERE "ProcessedAt" IS NULL;

-- Messages approaching max retries
SELECT COUNT(*) AS near_threshold
FROM "OutboxMessages"
WHERE "ProcessedAt" IS NULL
  AND "RetryCount" >= 3;

-- Messages permanently skipped (exceeded max retries + already marked processed)
SELECT COUNT(*) AS permanently_skipped
FROM "OutboxMessages"
WHERE "ProcessedAt" IS NOT NULL
  AND "RetryCount" >= "MaxRetries";

-- Largest lag (oldest unprocessed message)
SELECT MIN("CreatedAt") AS oldest_pending
FROM "OutboxMessages"
WHERE "ProcessedAt" IS NULL;
```

### Log Patterns

| Log Level | Pattern | Meaning |
|---|---|---|
| `Information` | `OutboxRelayService: Starting with pollingInterval=...` | Relay started |
| `Debug` | `OutboxRelayService: Processing {Count} pending outbox messages.` | Normal batch processing |
| `Warning` | `OutboxRelayService: Failed to publish message MessageId={Id}. Incrementing RetryCount.` | Transient ASB or DB failure |
| `Warning` | `OutboxRelayService: Failed to deserialize message Id={Id}, Type={Type}. Skipping.` | Message type resolution failure — check type name in outbox |
| `Error` | `OutboxRelayService: Message MessageId={Id} exceeded max retries ({Max}). Skipping permanently.` | Message permanently failed — requires manual investigation |
| `Warning` | `OutboxRelayService: IBus is null (ASB not configured). Skipping outbox processing.` | ASB not configured — relay idling (expected in local dev) |

---

## Recovery Procedures

### 1. Stuck Messages (constant retries without progress)

**Symptoms:**
- Log shows repeated `Failed to publish message` for the same `MessageId`
- Outbox depth is not decreasing

**Causes:**
- ASB connection string is misconfigured or expired
- Transient network issue between UserService and ASB
- Rebus bus is initialized but ASB namespace is unavailable

**Resolution:**
1. Check ASB connection string: `echo $Rebus__ConnectionString | grep -i "Endpoint=sb://"`
2. Verify ASB namespace is accessible: `curl -v https://{namespace}.servicebus.windows.net/`
3. If ASB is down, the relay will retry up to `MaxRetries` times. Messages will be marked as `ProcessedAt` after exceeding `MaxRetries` — **they are not lost**, they're logged for manual recovery.
4. To manually drain an outbox: see "Manual Outbox Drain" below.

### 2. Manual Outbox Drain

When the relay has permanently skipped messages (exceeded `MaxRetries`), you can drain them manually:

```bash
# 1. Connect to the database
docker exec -it kendo-postgres psql -U kendo -d kendo_users

# 2. Find skipped messages
SELECT "Id", "MessageId", "MessageType", "RetryCount", "LastError"
FROM "OutboxMessages"
WHERE "ProcessedAt" IS NOT NULL
  AND "RetryCount" >= 5;  # Replace 5 with your MaxRetries

# 3. To re-queue a skipped message (reset retry count):
UPDATE "OutboxMessages"
SET "RetryCount" = 0, "ProcessedAt" = NULL, "LastError" = NULL
WHERE "Id" = '<record-id>';

# 4. Verify the relay picks it up on the next polling cycle
```

### 3. Full Outbox Backlog

**Symptoms:** Outbox depth is growing faster than the relay can drain.

**Resolution:**
1. Increase `Relays__Outbox__BatchSize` to `100` (or higher, monitor DB CPU)
2. Decrease `Relays__Outbox__PollingIntervalSeconds` to `2`
3. If still growing, scale UserService replicas (each replica runs its own relay — optimistic concurrency handles duplicates safely since Worker idempotency enforces at-least-once)
4. **Temp escalation:** If ASB is completely unavailable and messages must not be lost, the outbox table serves as a durable buffer. Once ASB is restored, the relay drains naturally.

### 4. Duplicate Outbox Records

**Symptoms:** Multiple outbox records with the same `MessageId`.

**Cause:** The unique index `IX_OutboxMessages_MessageId` prevents duplicate `MessageId` values at the DB level. If you see duplicates, check the index was applied.

```sql
-- Check index exists
SELECT indexname, indexdef
FROM pg_indexes
WHERE tablename = 'OutboxMessages';

-- Find duplicate MessageIds (should return 0 — index enforces uniqueness)
SELECT "MessageId", COUNT(*)
FROM "OutboxMessages"
GROUP BY "MessageId"
HAVING COUNT(*) > 1;
```

If duplicates exist (pre-migration data), deduplicate by keeping the earliest `CreatedAt` entry:

```sql
DELETE FROM "OutboxMessages"
WHERE "Id" IN (
    SELECT "Id"
    FROM (
        SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "MessageId" ORDER BY "CreatedAt") AS rn
        FROM "OutboxMessages"
    ) dup
    WHERE dup.rn > 1
);
```

---

## Concurrency Model

The outbox relay uses **optimistic concurrency** — it reads pending rows, processes each, and updates with `WHERE "ProcessedAt" IS NULL`. If another replica processes the same row concurrently, the second replica's UPDATE affects 0 rows (the row was already marked processed), so it skips.

**Double-publishing is harmless** because the Worker's `UserCreatedEventHandler` enforces idempotency via `MessageId` — duplicates are silently discarded.

**Future enhancement:** Add `FOR UPDATE SKIP LOCKED` when querying pending messages to prevent concurrent replicas from processing the same batch.

---

## Rollback

### To revert to direct Rebus send (pre-outbox):

1. Revert the UsersController to use `IBus.Send()` directly (as in Day 07)
2. Remove `OutboxRepository` from DI
3. Drop the `OutboxMessages` table migration
4. Remove `OutboxRelayService` hosted service from Program.cs
5. Run migration rollback: `dotnet ef migrations remove --project src/UserService`
