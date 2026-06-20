# Architecture Spec — Day 27 — Gateway JWT Auth Refactor

> **Carry-forward resolution:** CF-2 — `IssuerSigningKeyResolver` anti-pattern, user JWT issuance, key rotation  
> **Roadmap phase:** 05 — AI/Vector Service (FastAPI)  
> **Date:** 2026-06-20  
> **Type:** Remediation spec — refactoring existing .NET auth code  
> **Depends on:** `docs/architecture/day_17_spec.md` (original JWT auth spec)

---

## Milestone Scope

- **What:** Three refactors to the Gateway JWT auth layer from Day 17's open questions:
  1. Replace `IssuerSigningKeyResolver`'s `BuildServiceProvider()` anti-pattern with a closure-based DI pattern
  2. Add user JWT issuance endpoint (`POST /api/auth/token`) — currently only validation exists
  3. Add `KeyRotationBackgroundService` — currently key rotation is manual only
- **Why now:** FastAPI workload (M5.7+) validates JWTs against the Gateway's JWKS endpoint. If the key resolver or JWKS schema changes after W1 ships, FastAPI breaks. Freeze the contract before workloads land.
- **Explicitly out of scope:**
  - New workload endpoints (M5.7+)
  - User registration / identity management (remains external)
  - mTLS or service-token auth (future from M5.11 spec direction)

---

## Layer Changes

| Layer | Service | Change |
|-------|---------|--------|
| Application | `src/Gateway/` | Refactor `IssuerSigningKeyResolver` to closure-based pattern |
| Application | `src/Gateway/` | Add `POST /api/auth/token` user JWT issuance endpoint |
| Application | `src/Gateway/` | Add `KeyRotationBackgroundService` for automatic key rotation |

---

## Data Contracts

### New endpoint: `POST /api/auth/token` — Issue user JWT

**Request:**
```json
{
  "user_id": "uuid",
  "roles": ["string"],
  "ttl_seconds": 3600
}
```

**Response — 200 OK:**
```json
{
  "access_token": "string (JWT, RS256)",
  "token_type": "Bearer",
  "expires_in": 3600
}
```

**Error responses:** Standard RFC 7807 — `400` (invalid request), `401` (Gateway service-JWT required).

**Auth:** Endpoint itself is protected by a Gateway service-JWT scope (`admin:token`). Only Worker and UserService can mint user tokens via this endpoint. User-facing token issuance is out of scope.

### Key rotation contract (unchanged from Day 17):

- Rotation schedule: `KENDO__JWT__ROTATION_DAYS` (default 90 days)
- Overlap window: 2-key overlap during rotation — new key is primary for signing, old key remains in JWKS for token validation until expiry
- JWKS endpoint: `/.well-known/jwks.json` — schema unchanged (RFC 7517)

---

## Implementation Plan (Commit Units)

### Unit 1 — Refactor IssuerSigningKeyResolver (closure-based DI)

**Files:**
- `src/Gateway/Infrastructure/Auth/IssuerSigningKeyResolver.cs`
- `src/Gateway/Infrastructure/Auth/RsaKeyProvider.cs`

**Change summary:** Remove `BuildServiceProvider()` anti-pattern. Inject `IServiceProvider` or `IConfiguration` + `IRsaKeyProvider` directly via constructor. The resolver becomes a standard DI-registered singleton with a closure over the injected services.

**Gate command:** `dotnet test tests/Kendo.Tests --filter "Category=Auth"` — all auth tests pass.

**Commit message:**
```
refactor(gateway): replace IssuerSigningKeyResolver BuildServiceProvider with closure-based DI

Injects IServiceProvider and IRsaKeyProvider via constructor instead of calling
BuildServiceProvider() at runtime. Eliminates the anti-pattern flagged in Day 17 open
questions. JWKS endpoint contract unchanged.

Day 27 — CF-2 Unit 1 of 3 | Milestone: CF-2 (carry-forward)
Coverage: 100% auth tests passing
Lint: clean
```

### Unit 2 — Add user JWT issuance endpoint

**Files:**
- `src/Gateway/Controllers/TokenController.cs` — new
- `src/Gateway/Infrastructure/Auth/TokenService.cs` — new
- `src/Gateway/Infrastructure/Auth/ITokenService.cs` — new interface

**Change summary:** Implement `POST /api/auth/token` that accepts a `{ user_id, roles, ttl_seconds }` body, validates the caller has the `admin:token` scope, mints a JWT with the Gateway's private key, and returns the token. Uses the same `RsaKeyProvider` as the existing auth pipeline.

**Gate command:** `dotnet test tests/Kendo.Tests --filter "Category=Auth"` — includes new token-issuance tests (≥ 5: happy path, expired caller, insufficient scope, invalid body, ttl bounds).

**Commit message:**
```
feat(gateway): add POST /api/auth/token user JWT issuance endpoint

Mints RS256 JWTs signed with the Gateway's private key. Protected by admin:token
scope — only service JWTs can mint user tokens. Same key provider as existing
JWT validation and JWKS endpoint. Resolves Day 17 open question: user JWT issuance
(previously validation-only).

Day 27 — CF-2 Unit 2 of 3 | Milestone: CF-2 (carry-forward)
Coverage: 100% (≥5 new token tests)
Lint: clean
```

### Unit 3 — Add KeyRotationBackgroundService

**Files:**
- `src/Gateway/Infrastructure/Auth/KeyRotationBackgroundService.cs` — new
- `src/Gateway/Infrastructure/Auth/RsaKeyProvider.cs` (modified — add rotation method)

**Change summary:** Implement a `BackgroundService` that checks the keypair's age on a periodic timer (check interval default 1h, rotation threshold from `KENDO__JWT__ROTATION_DAYS`). When a keypair exceeds the threshold, generates a new RSA-2048 keypair, persists it, and publishes the new public key to the JWKS endpoint. During the overlap window, both old and new keys appear in the JWKS response. Old key is removed from JWKS after the overlap window expires.

**Gate command:** `dotnet test tests/Kendo.Tests --filter "Category=Auth"` — includes rotation tests (key generated after threshold, overlap window, old key removed after expiry).

**Commit message:**
```
feat(gateway): add KeyRotationBackgroundService for automatic JWK rotation

Automatically rotates RSA-2048 keypair on KENDO__JWT__ROTATION_DAYS schedule
(default 90d). Two-key overlap window ensures tokens signed with the old key
remain valid during rotation. JWKS endpoint (/.well-known/jwks.json) updated
atomically. Resolves Day 17 open question (previously manual-only).

Day 27 — CF-2 Unit 3 of 3 | Milestone: CF-2 (carry-forward)
Coverage: 100% (≥5 new rotation tests)
Lint: clean
```

---

## Success Checklist

| # | Criterion | Maps to |
|---|-----------|---------|
| 1 | `IssuerSigningKeyResolver` no longer calls `BuildServiceProvider()` | CF-2 resolution |
| 2 | `POST /api/auth/token` returns valid RS256 JWT with correct claims | CF-2 resolution |
| 3 | Token issuances with expired/invalid caller JWT return 403 RFC 7807 | Security baseline |
| 4 | `KeyRotationBackgroundService` generates new keypair at threshold and publishes to JWKS | CF-2 resolution |
| 5 | Old key remains in JWKS during overlap window; removed after window expires | CF-2 resolution |
| 6 | FastAPI JWKS validation client (existing) still passes with the refactored JWKS endpoint | Regression guard — test with FastAPI integration |
| 7 | All Phase 01–05 foundation acceptance criteria still pass | Inherited |

---

## Resilience Mandate

- Key rotation BackgroundService has a retry loop for persistence failures (3 retries, 10s delay) — a failed rotation retries on the next check interval
- Token issuance endpoint uses the same `HttpContext.RequestAborted` cancellation as existing controllers
- No new circuit breaker or retry policies needed — this is Gateway-internal auth logic, not an external dependency call

---

## Depends on

- `docs/architecture/day_17_spec.md` — original JWT auth spec; this spec refactors it but preserves all contracts
- `src/Gateway/Infrastructure/Auth/` — the directory containing all files to be modified
