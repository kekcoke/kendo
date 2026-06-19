# Day 17 Runbook — Gateway JWT Auth + FastAPI AI Integration (M0.1+M0.2)
> **Phase 4 artifact.** Created by DevOps/SRE agent.  
> **Milestone:** M0.1+M0.2 — Gateway JWT auth + AI integration

---

## Overview

Day 17 implements the Gateway-side foundations for the FastAPI reconciliation phase:
- **M0.1 (CCD-2):** RS256 JWT keypair generation, JWKS endpoint, and token validation
- **M0.2:** Typed `IFastAPIClient` with Polly resilience, 4 new controllers (RAG, User Search, Assistant, Intent Advisory), and service-JWT minter (CCD-3)

---

## New Infrastructure Resources

| Resource | Type | Purpose | Consumed By |
|---|---|---|---|
| `secrets/jwt-private.pem` | File (RSA-2048 PEM) | JWT signing keypair | Gateway (`RsaKeyProvider`) |
| `/.well-known/jwks.json` | HTTP endpoint | Public key distribution (RFC 7517 JWK format) | FastAPI, future services |
| `IFastAPIClient` | Typed HTTP client (Polly) | Resilient calls to FastAPI service | Gateway controllers |
| `ServiceJwtMinter` | In-memory service | Short-lived `admin:writes` JWTs for service-to-service auth | Gateway → UserService |

---

## Configuration

### Environment Variables (Gateway)

| Variable | Default | Description |
|---|---|---|
| `Kendo__Jwt__PrivateKeyPath` | `secrets/jwt-private.pem` | Path to RSA private key PEM file |
| `Kendo__Jwt__Issuer` | `https://gateway.local/.well-known/jwks.json` | JWT issuer claim |
| `Kendo__Jwt__Audience` | `kendo.api` | JWT audience claim |
| `Kendo__Jwt__RotationDays` | `90` | Key rotation interval (days) |
| `Kendo__Jwt__TokenLifetimeMinutes` | `60` | User JWT lifetime (minutes) |
| `Kendo__Jwt__ServiceTokenLifetimeSeconds` | `60` | Service JWT lifetime (seconds) |
| `Kendo__FastApi__BaseUrl` | `http://fastapi:8000` | FastAPI service base URL (internal Docker network) |
| `Kendo__FastApi__TimeoutSeconds` | `30` | HTTP timeout for FastAPI calls |
| `Kendo__FastApi__CircuitBreakerFailures` | `3` | Consecutive failures before circuit breaker opens |
| `Kendo__FastApi__CircuitBreakerBreakSeconds` | `30` | Circuit breaker cooldown duration |

---

## Health Checks

### New Endpoints

| Endpoint | Method | Auth | Description |
|---|---|---|---|
| `/.well-known/jwks.json` | GET | Anonymous | Returns public RSA key in JWK format (RFC 7517) |

### Existing Health Endpoints (unchanged)

| Endpoint | Expected | Description |
|---|---|---|
| `/health/live` | 200 OK | Liveness probe (no dependency checks) |
| `/health/ready` | 200 OK | Readiness probe |

---

## Resilience & Failure Modes

### FastAPI Client (Polly v8 Pipeline)

| Policy | Config | Behavior |
|---|---|---|
| **Timeout** | 30s | `TimeoutRejectedException` → logged → circuit breaker counts as failure |
| **Retry** | 3 attempts, exponential + jitter | Transient fault predicate: 408, 429, 5xx, `HttpRequestException`, `TimeoutRejectedException` |
| **Circuit Breaker** | 3 failures → 30s break | `BrokenCircuitException` → 503 + RFC 7807 `FastApiError` |
| **Recovery** | Half-open after 30s | Single probe request; success → closed, failure → open again |

### Intent Advisory Middleware (W4)

| Condition | Behavior |
|---|---|
| FastAPI returns classification | Tags `HttpContext.Items` with route + confidence |
| FastAPI timeout | Logs warning, sets `"fallback"` — no 500 |
| FastAPI circuit breaker open | Logs warning, sets `"fallback"` — no 500 |
| FastAPI misbehaving (invalid JSON) | `try/catch` catches `HttpRequestException`, logs, fallback |

### Key Rotation

| Event | Procedure |
|---|---|
| Planned rotation (90 days) | Call `RsaKeyProvider.RotateKey()` → new keypair written to PEM → old public key kept in JWKS for 7-day overlap window |
| Emergency rotation | Same as planned; existing tokens valid until expiry |

---

## Verification Steps

### Local Verification
```bash
# 1. Build
dotnet build src/Kendo.slnx

# 2. Run unit tests (non-chaos)
dotnet test src/Kendo.slnx --filter "Category!=Chaos&Category!=Infra"

# 3. Docker Compose up
docker compose up -d

# 4. Verify JWKS endpoint
curl -s http://localhost:5000/.well-known/jwks.json | jq .
# Expected: {"keys":[{"kty":"RSA","use":"sig","alg":"RS256","kid":"...","n":"...","e":"AQAB"}]}

# 5. Verify JWT validation (mint + validate)
# (Requires integration test or manual token creation)
```

### CI Verification
The existing CI pipeline in `.github/workflows/ci.yml` covers:
- Build solution → must exit 0
- Unit tests (Category=Unit) → 119 tests passing
- Docker compose up → 6 services healthy
- Health endpoint verification → all /health/live and /health/ready pass

**No CI changes required for Day 17** — existing pipeline handles the Gateway build and Docker Compose health checks. The JWKS endpoint is verified implicitly as part of the Gateway health check.

---

## Rollback Plan

### Rollback Steps
```bash
# 1. Revert the feature branch commit
git revert <commit-sha>
git push origin develop

# 2. Remove generated keypair (if created)
rm -f secrets/jwt-private.pem

# 3. Restart services
docker compose down --volumes
docker compose up -d
```

### Key Considerations
- **JWT keypair is ephemeral** — loss of `secrets/jwt-private.pem` breaks all existing JWTs, requiring re-issuance. In production, store in Azure Key Vault / K8s Secret.
- **Service JWTs are short-lived (60s)** — no cached tokens survive a rollback longer than 30s.
- **IFastAPIClient is not called yet** — no FastAPI service exists, so rolling back the client has zero production impact.

---

## Monitoring & Alerting

| Metric | Source | Action |
|---|---|---|
| FastAPI circuit breaker state | `Logger.LogError("FastAPI circuit breaker OPEN")` | Investigate FastAPI connectivity |
| FastAPI timeout frequency | `Logger.LogWarning("FastAPI retry")` (3+ retries) | Check FastAPI latency / resources |
| JWKS endpoint unreachable | Gateway `/health/ready` failure | Check Gateway process and keypair |
| JWT validation failures | `Logger.LogWarning` from `JwtBearerHandler` | Check token issuer / audience / expiry |

---

## Known Issues

- **`IssuerSigningKeyResolver` uses `BuildServiceProvider()`** — known anti-pattern (flagged in `day_17_spec.md`). Should be refactored to inject `IServiceProvider` via closure.
- **User JWT issuance not implemented** — Gateway validates tokens but cannot issue them. External IdP integration deferred.
- **Key rotation has no BackgroundService** — `RotateKey()` exists but is not scheduled. Rotation is manual (`KENDO__JWT__ROTATION_DAYS` is informational only).

---

## Related Docs

| Document | Path |
|---|---|
| Day 17 Architecture Spec | `docs/architecture/day_17_spec.md` |
| FastAPI Service Spec | `docs/architecture/fastapi_rag_service_spec.md` |
| Day 18 Spec (Events + Queue) | `docs/architecture/day_18_spec.md` (planned) |
| Day 20 Spec (UserService Entities) | `docs/architecture/day_20_spec.md` (planned) |
| CI Pipeline | `.github/workflows/ci.yml` |
