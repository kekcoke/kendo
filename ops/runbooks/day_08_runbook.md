# Day 08 — Ops Runbook: M2.3 Background Consumer with Idempotency

## Overview
The Worker Service now consumes `UserCreatedEvent` messages via Rebus/ASB with idempotency enforcement.
Each message is tracked in the `IdempotencyRecords` table. Duplicate messages are silently discarded.
Crash recovery re-processes messages left in `Processing` state.

## New Dependencies
- Worker now requires `ConnectionStrings__DefaultConnection` (PostgreSQL) for the `WorkerDbContext`
- Worker handles user status updates directly via `AppDbContext` (shared `kendo_users` DB)

## Rollback Plan

### If Worker crashes on startup:
1. **Check DB config**: Verify `ConnectionStrings__DefaultConnection` is set on the Worker container
2. **Check migration**: Ensure `AddIdempotencyRecords` migration was applied: `docker compose run --rm worker dotnet ef database update --project src/Worker`
3. **Rollback code**: `git revert <merge-sha>` and deploy

### If duplicate users appear (idempotency failure):
1. Check `IdempotencyRecords` table for duplicate `MessageId` entries:
   ```sql
   SELECT "MessageId", COUNT(*) FROM "IdempotencyRecords" GROUP BY "MessageId" HAVING COUNT(*) > 1;
   ```
2. If duplicates found, the unique constraint on `MessageId` PK should prevent it — investigate handler logic

### If messages go to DLQ:
- Check Worker logs for `IdempotencyStatus.Failed` entries
- Common causes: unknown `UserId`, DB connectivity issues, Rebus transport errors
- DLQ consumer (M2.4) is deferred — manual DLQ drain procedure is:
  1. Inspect DLQ messages via Azure Service Bus Explorer
  2. Fix root cause (e.g., seed missing Users)
  3. Re-submit corrected messages to `kendo-events` queue

## Health Check Verification
```bash
curl -fsS http://localhost:5000/health/live
curl -fsS http://localhost:5001/health/live
curl -fsS http://localhost:5002/health/live
```

## Smoke Test (End-to-End)
```bash
# 1. Create a user (returns 202 with Location header)
LOCATION=$(curl -s -D - -X POST http://localhost:5001/api/users \
  -H 'Content-Type: application/json' \
  -d '{"email":"smoke@test.com","displayName":"Smoke"}' \
  | grep -i location | awk '{print $2}' | tr -d '\r')

echo "Status URL: $LOCATION"

# 2. Poll status until completed (should work with or without ASB — idempotency is local DB)
sleep 2
curl -s "$LOCATION" | jq .
```

## Recovery Procedures

### Worker DB Connection Failure
- Circuit breaker trips after 3 failures → 30s break
- Rebus will retry the message (max retries default) then move to DLQ
- Recovery: ensure PostgreSQL is running and `ConnectionStrings__DefaultConnection` is correct

### Worker Crash Mid-Processing
- Message left in `IdempotencyRecords` with `Status = Processing`
- On restart, the handler detects `Processing` status and re-processes the message
- No manual intervention required — automatic crash recovery

### ASB Unavailable
- Worker starts without ASB connection: `IBus` resolves to null gracefully (graceful skip)
- No messages are consumed in this state — re-enable by setting `Rebus__ConnectionString`
- When re-enabled, pending messages in `kendo-events` queue are processed normally

## Monitoring
- **Worker health**: `GET /health/live` and `/health/ready` on port 5002
- **Idempotency failures**: Search logs for `IdempotencyStatus.Failed` or `Recording as Failed`
- **Crash recovery**: Search logs for `Crash recovery` warning
