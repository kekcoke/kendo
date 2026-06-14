# Day 07 — Ops Runbook: M2.2 First Async Endpoint

## Overview
User registration endpoint refactored from synchronous to async 202-Accepted pattern.  
POST to UserService persists the user and publishes a `UserCreatedEvent` via Rebus/ASB.

## Rollback Plan

### If POST /api/users returns errors:
1. **Rollback DB**: Execute `DELETE FROM "Users" WHERE "CreatedAt" > NOW() - INTERVAL '5 minutes';`
2. **Rollback code**: `git revert <merge-sha>` and deploy
3. **Verify**: `curl -X POST http://localhost:5001/api/users -H 'Content-Type: application/json' -d '{"email":"t@t.com","displayName":"T"}'` should return 202

### If UserCreatedEvent publishing fails:
- The controller already handles `IBus == null` gracefully (local dev without ASB)
- In production, check `Rebus__ConnectionString` env var is set on UserService
- Check ASB queue `kendo-events` exists in the Azure namespace

## Health Check Verification
```bash
curl -fsS http://localhost:5000/health/live
curl -fsS http://localhost:5001/health/live
curl -fsS http://localhost:5002/health/live
```

## Smoke Test
```bash
curl -s -X POST http://localhost:5001/api/users \
  -H 'Content-Type: application/json' \
  -d '{"email":"smoke@test.com","displayName":"Smoke"}' | jq .
```

## Recovery Procedures

### DB Connection Failure
- Circuit breaker trips after 3 failures → 30s break
- API returns `503 Service Unavailable` with RFC 7807 body
- No action needed: auto-recovery after break duration

### ASB Unavailable
- Controller logs warning, still returns 202
- User is created in DB, event will be lost without outbox (M2.5 deferred)
- Re-run setup when ASB is available: ensure `Rebus__ConnectionString` is set
