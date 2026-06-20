# Day 26 Runbook — Gateway JWT Auth Refactor (CF-2)
> **Phase 4 artifact.** Created by DevOps/SRE agent.
> **Milestone:** CF-2 — Gateway JWT Auth Refactor
> **Date:** 2026-06-20

---

## Overview

Day 26 resolves the Day 17 carry-forward items for Gateway JWT auth:
1. **Unit 1** — Remove `BuildServiceProvider()` anti-pattern from `IssuerSigningKeyResolver` using `OptionsBuilder.Configure<>()` closure
2. **Unit 2** — Add `POST /api/auth/token` user JWT issuance endpoint (protected by `AdminToken` policy)
3. **Unit 3** — Add `KeyRotationBackgroundService` with two-key overlap window and meta-file persistence

---

## Infrastructure Changes

| Resource | Type | Change |
|---|---|---|
| `secrets/jwt-private.pem` | File (RSA-2048 PEM) | Now backed up to `secrets/jwt-private.previous.pem` on rotation |
| `secrets/jwt-private.meta.json` | File (JSON) | **New** — tracks rotation timestamps, current/previous key IDs, overlap expiry |
| `/.well-known/jwks.json` | HTTP endpoint | Now returns **all valid keys** (current + previous within 7-day overlap window) |

---

## New Endpoints

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| `POST` | `/api/auth/token` | `AdminToken` policy (service-JWT with `admin:token` scope) | Mints RS256 user JWTs |

### Token issuance request/response

**Request:**
```json
{
  "userId": "uuid",
  "roles": ["user"],
  "ttlSeconds": 3600
}
```

**Response — 200 OK:**
```json
{
  "accessToken": "string (JWT, RS256)",
  "tokenType": "Bearer",
  "expiresIn": 3600
}
```

**Error responses:** Standard RFC 7807 — `400` (invalid request), `403` (insufficient scope).

---

## Key Rotation Details

| Parameter | Default | Config |
|-----------|---------|--------|
| Rotation threshold | 90 days | `Kendo:Jwt:RotationDays` |
| Check interval | 1 hour | Hard-coded in `KeyRotationBackgroundService` |
| Overlap window | 7 days | Hard-coded in `RsaKeyProvider` |
| Persistence retries | 3 (10s delay) | Hard-coded in `KeyRotationBackgroundService` |

### Rotation flow
1. `KeyRotationBackgroundService` checks `RsaKeyProvider.ShouldRotate()` every hour
2. If threshold exceeded, calls `RotateKey()`:
   a. Current key saved as "previous" with 7-day expiry
   b. New RSA-2048 keypair generated
   c. Previous PEM backed up to `secrets/jwt-private.previous.pem`
   d. Current PEM written to `secrets/jwt-private.pem`
   e. Meta JSON written to `secrets/jwt-private.meta.json`
3. JWKS endpoint returns both keys during overlap window
4. Old key removed from JWKS after 7-day overlap expires

### Recovery
- On Gateway restart, `RsaKeyProvider.LoadMeta()` reads `jwt-private.meta.json` to restore overlap state
- If previous key file is missing or corrupt, overlap window is silently skipped (current key only)
- Meta file corruption does not block startup — rotation will establish new state

---

## Chaos Scenarios

| Scenario | Expected behavior | How to test |
|---|---|---|
| **Rotation during high traffic** | Atomic PEM+meta writes under lock; no partial state visible | Concurrent requests to JWKS endpoint during rotation |
| **Restart during overlap window** | Previous key restored from meta + backup PEM; both keys in JWKS | Restart Gateway, check `/.well-known/jwks.json` |
| **Disk full during rotation** | Retry up to 3 times (10s interval); backoff to next check interval | Fill disk, trigger rotation, observe retry logs |
| **Token issued with old key after rotation** | Old key still in JWKS overlap → validation succeeds for 7 days | Mint token, rotate, validate token against JWKS |

---

## Rollback Plan

1. **Revert merge:** `git revert <merge-sha>` on `develop`
2. **Restore previous key:** If keys were rotated between merge and revert, restore `secrets/jwt-private.previous.pem` to `secrets/jwt-private.pem`
3. **Clear meta:** Delete `secrets/jwt-private.meta.json` (will be regenerated on next startup)
4. **Redeploy Gateway**
5. **Verify:** `/.well-known/jwks.json` returns single key; `POST /api/auth/token` returns 403 without valid service-JWT
