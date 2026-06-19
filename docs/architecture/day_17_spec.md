# Architecture Spec — Day 17 — Gateway: AI Integration + JWT Auth

> **Milestone:** M0.1, M0.2 — Gateway JWT auth + AI integration  
> **Roadmap phase:** 00 — Pre-FastAPI Reconciliation (see roadmap header note)  
> **Date:** 2026-06-16  
> **Architect:** Platform Architect (Orchestrator-delegated)  
> **Depends on:** `docs/architecture/fastapi_rag_service_spec.md` § CCD-2, CCD-3; consumes entities from `day_20_spec.md`; consumes events from `day_18_spec.md`

---

## Why this spec exists

The FastAPI spec (M5.7–M5.14) assumes the Gateway can (a) validate user JWTs and (b)
proxy/relay authenticated calls to FastAPI. The audit confirms **both are entirely
absent** today: no `JwtBearer` registration, no `IFastAPIClient`, no `RagController`,
no advisory middleware. This spec authors the Gateway side of the FastAPI integration
and the JWT trust root (CCD-2) the rest of the platform depends on.

It is the **third** of the four reconciliatory specs and is authored after
`day_20_spec.md` (entities) and `day_18_spec.md` (events) because the new controllers
and the service-JWT minter reference both.

---

## Milestone Scope

- **Roadmap milestone:** M0.1 (JWT auth) + M0.2 (Gateway → FastAPI integration)
- **Components touched:**
  - `src/Gateway/Program.cs` — adds `AddKendoJwt()`, `AddKendoFastApiClient()`, `AddKendoServiceJwtMinter()`, `AddKendoAdminScopePolicies()`, 4 new controllers, `IntentAdvisoryMiddleware`
  - `src/Shared/Kendo.Shared.Authentication/JwtOptions.cs` — new
  - `src/Shared/Kendo.Shared.Authentication/AddKendoJwt.cs` — new
  - `src/Shared/Kendo.Shared.Authentication/ServiceJwtMinter.cs` — new (CCD-3)
  - `src/Shared/Kendo.Shared.Authentication/AdminScopePolicies.cs` — new (consumed by `day_20_spec.md`)
  - `src/Shared/Kendo.Shared.Http/FastApiOptions.cs` — new
  - `src/Shared/Kendo.Shared.Http/IFastAPIClient.cs` — new
  - `src/Shared/Kendo.Shared.Http/FastAPIClient.cs` — new (with Polly)
  - `src/Shared/Kendo.Shared.Http/AddKendoFastApiClient.cs` — new
  - `src/Gateway/Controllers/RagController.cs` — new (W1, W2)
  - `src/Gateway/Controllers/UserSearchController.cs` — new (W3)
  - `src/Gateway/Controllers/AssistantController.cs` — new (W6)
  - `src/Gateway/Middleware/IntentAdvisoryMiddleware.cs` — new (W4)
  - `src/Gateway/WellKnown/JwksEndpoint.cs` — new (CCD-2, serves `/.well-known/jwks.json`)
  - `src/Gateway/KeyManagement/RsaKeyProvider.cs` — new (CCD-2, keypair generation + rotation)
  - `src/Shared/Kendo.Shared.Http/FastApiExceptionMapper.cs` — new (FastAPI RFC 7807 → Gateway RFC 7807)
  - `docker-compose.yml` — new env vars
  - `.env.example` — `KENDO__JWT__...`, `KENDO__FASTAPI__...`
- **Explicitly out of scope:**
  - UserService entity/migration work (covered by `day_20_spec.md`)
  - Worker handler implementations (covered by `day_19_spec.md`)
  - Service Bus event types (covered by `day_18_spec.md`)
  - FastAPI service code (covered by `fastapi_rag_service_spec.md`)

---

## Layer Changes

| Layer | Service | Change |
|-------|---------|--------|
| Auth (trust root) | Gateway | RS256 keypair, JWKS endpoint, JWT validation, service-JWT minter |
| Auth (policies) | Kendo.Shared | `admin:writes` scope policy with `token_use=service` enforcement |
| HTTP (client) | Kendo.Shared | `IFastAPIClient` / `FastAPIClient` with Polly timeout + circuit breaker |
| API (public) | Gateway | `RagController` (W1, W2), `UserSearchController` (W3), `AssistantController` (W6) |
| Middleware | Gateway | `IntentAdvisoryMiddleware` (W4, advisory-only, hard-coded .NET fallback) |
| Well-known | Gateway | `JwksEndpoint` at `/.well-known/jwks.json` |
| Configuration | docker-compose | New env: `KENDO__JWT__PRIVATE_KEY_PATH`, `KENDO__JWT__ISSUER`, `KENDO__JWT__AUDIENCE`, `KENDO__FASTAPI__BASE_URL`, etc. |

---

## Data Contracts

### JWT — CCD-2 (RS256, Gateway-served JWKS)

#### Key management

The Gateway generates an `RSA-2048` keypair on first boot if none exists at
`KENDO__JWT__PRIVATE_KEY_PATH` (default `secrets/jwt-private.pem`). The public key
is published at `/.well-known/jwks.json` in RFC 7517 format.

```csharp
namespace Kendo.Shared.Authentication;

public class RsaKeyProvider
{
    public RsaSecurityKey GetCurrentSigningKey();
    public RsaSecurityKey GetPublicKey();          // for JWKS exposure
    public void RotateKey();                        // 90-day rotation
    public string CurrentKeyId();                   // "kid" header
}
```

> **Rotation policy:** when `RotateKey()` is called (manual or scheduled via
> `KENDO__JWT__ROTATION_DAYS`, default 90), the new keypair is generated and the
> old public key is **kept valid for 7 days** (the overlap window) before being
> removed from JWKS. Tokens issued under the old key remain valid until they
> expire. FastAPI's JWKS cache (1-hour in-memory, refreshed on `kid` miss)
> tolerates this naturally.

#### `JwtOptions` (configuration)

```csharp
public class JwtOptions
{
    public string PrivateKeyPath { get; set; } = "secrets/jwt-private.pem";
    public string Issuer { get; set; } = "https://gateway.local/.well-known/jwks.json";
    public string Audience { get; set; } = "kendo.api";
    public int RotationDays { get; set; } = 90;
    public int TokenLifetimeMinutes { get; set; } = 60;
    public int ServiceTokenLifetimeSeconds { get; set; } = 60;  // short-lived for service JWTs
}
```

Bound via `services.Configure<JwtOptions>(config.GetSection("Kendo:Jwt"))`.

#### `AddKendoJwt` extension

```csharp
public static IServiceCollection AddKendoJwt(this IServiceCollection services, IConfiguration config)
{
    services.Configure<JwtOptions>(config.GetSection("Kendo:Jwt"));
    services.AddSingleton<RsaKeyProvider>();

    var jwt = config.GetSection("Kendo:Jwt").Get<JwtOptions>() ?? new JwtOptions();
    services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(o =>
        {
            o.RequireHttpsMetadata = false;          // dev-friendly; prod uses HTTPS via NGINX
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwt.Issuer,
                ValidateAudience = true,
                ValidAudience = jwt.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2),
                IssuerSigningKeyResolver = (token, securityToken, kid, parameters) =>
                {
                    // Resolves the public key by "kid" from the in-memory key provider.
                    var provider = services.BuildServiceProvider().GetRequiredService<RsaKeyProvider>();
                    return new[] { provider.GetPublicKey() };
                }
            };
        });

    return services;
}
```

> **Note:** the `BuildServiceProvider()` call inside the resolver is a known
> anti-pattern. The recommended fix is to inject `IServiceProvider` into the
> `IssuerSigningKeyResolver` via a closure. This is the implementation the
> spec expects; the final code is owned by the developer.

#### `JwksEndpoint` (well-known route)

A minimal-API endpoint that returns the current public key (and any in-overlap
keys) in JWK format.

```csharp
app.MapGet("/.well-known/jwks.json", (RsaKeyProvider keys) =>
{
    var jwks = new
    {
        keys = new[]
        {
            JwkFromRsa(keys.CurrentKeyId, keys.GetPublicKey()),
            // optionally: include previous key during overlap window
        }
    };
    return Results.Json(jwks);
}).AllowAnonymous();
```

#### `ServiceJwtMinter` (CCD-3, option C)

The Gateway's `IFastAPIClient` to `UserService` (for the `EmbeddingAdminController`
writes) mints a short-lived service JWT signed with the Gateway's private key.

```csharp
public interface IServiceJwtMinter
{
    string MintAdminWritesToken();  // claims: { scope: "admin:writes", token_use: "service", aud: "kendo.api", iss: ..., exp: now+60s }
}
```

The minter uses the same `RsaKeyProvider` as the user JWT path. The token is
cached in-memory for 30 seconds (its 60-second lifetime, minus a safety margin)
and re-minted on demand.

### FastAPI client — CCD-2 + M5.5 parity

#### `FastApiOptions`

```csharp
public class FastApiOptions
{
    public string BaseUrl { get; set; } = "http://fastapi:8000";  // internal docker network
    public int TimeoutSeconds { get; set; } = 30;
    public int CircuitBreakerFailures { get; set; } = 3;
    public int CircuitBreakerBreakSeconds { get; set; } = 30;
}
```

#### `IFastAPIClient`

The contract between the Gateway and the FastAPI service. Every method maps to a
specific FastAPI workload (W1–W7).

```csharp
public interface IFastAPIClient
{
    // W1 — Event Ingestion RAG
    Task<EventIngestionResult> IngestEventAsync(EventIngestionRequest request, CancellationToken ct);
    IAsyncEnumerable<EventIngestionStreamChunk> IngestEventStreamAsync(EventIngestionRequest request, CancellationToken ct);

    // W2 — Event Conflict & Schedule Reasoning
    Task<EventValidationResult> ValidateEventAsync(Guid eventId, EventValidationRequest request, CancellationToken ct);

    // W3 — User Profile Semantic Search
    Task<UserSearchResult> SearchUsersAsync(UserSearchRequest request, CancellationToken ct);

    // W4 — User Intent Classification (advisory)
    Task<IntentClassificationResult> ClassifyIntentAsync(IntentClassificationRequest request, CancellationToken ct);

    // W6 — Document Q&A / Onboarding Assistant
    Task<AssistantAnswerResult> AskAssistantAsync(AssistantQuestionRequest request, CancellationToken ct);

    // W7 (called by Worker, not Gateway, but kept here for completeness)
    // — see IFastAPISummarizationClient in Kendo.Shared (Worker uses the same interface)
}
```

#### `FastAPIClient` implementation — Polly + RFC 7807 mapping

```csharp
public class FastAPIClient : IFastAPIClient
{
    private readonly HttpClient _http;
    private readonly AsyncPolicyWrap<HttpResponseMessage> _policy;
    private readonly FastApiExceptionMapper _mapper;

    // Constructor:
    //   - registers a named HttpClient ("FastAPI") with the base URL + default headers
    //   - wraps the underlying HttpClient in:
    //       TimeoutPolicy (FastApiOptions.TimeoutSeconds)
    //       RetryPolicy (3 attempts, exponential backoff + jitter, transient-fault predicate
    //                    — 5xx, 408, 429, RequestException; NOT 4xx other than 408/429)
    //       CircuitBreakerPolicy (3 consecutive failures → 30s break; recovers on half-open success)
    //   - injects IServiceJwtMinter for the EmbeddingAdminController path
    //   - injects FastApiExceptionMapper that translates FastAPI's RFC 7807 responses
    //     to the Gateway's existing KendoProblemDetails (no shape change for callers)
}
```

> **Polly parity:** the spec reuses the existing `Kendo.Shared.Resilience` pipeline
> (Day 03) at the bus level. The `FastAPIClient` is the first **service-to-service**
> caller and uses its own Polly policy chain configured via `FastApiOptions`. The
> shape mirrors the .NET-side pipeline so chaos tests and dashboards look the same.

### Public API — new Gateway controllers

All controllers are authenticated via `[Authorize]` (no anonymous), use the existing
`ProblemDetailsMiddleware` (Day 05) for error responses, and emit OpenTelemetry
spans with `kind=client` and the FastAPI URL as the `peer`.

#### `RagController` (W1, W2)

| Method | Route | Description | Auth |
|---|---|---|---|
| `POST` | `/api/events/ingest` | Forward free-form text to FastAPI W1; validate structured JSON; emit `EventIngestedEvent` on `kendo-events-ai`; return 202 Accepted with `Location: /api/events/{id}` | JWT (any role) |
| `GET` | `/api/events/ingest/{id}/stream` | SSE stream of the W1 inference (token-by-token) | JWT (any role, owner only) |
| `POST` | `/api/events/{id}/validate` | Forward candidate event to FastAPI W2; persist `EventValidation`; emit `EventValidatedEvent`; return 200 with the validation result | JWT (any role, owner only) |

> **The 202 pattern is the same as Day 07** (`POST /api/users` returns 202). The
> Gateway never blocks on the LLM response — the worker does that. The Gateway's
> job is to (a) call FastAPI, (b) validate the structured JSON, (c) emit
> `EventIngestedEvent`, (d) return 202.

#### `UserSearchController` (W3)

| Method | Route | Description | Auth |
|---|---|---|---|
| `GET` | `/api/users/search?q={query}&top={N}` | Hybrid pgvector + BM25 search; returns top-N user IDs + relevance | JWT (any role) |
| `GET` | `/api/users/search/{id}` | Resolve a search-result ID to the full user record via `UserService` | JWT (any role) |

#### `AssistantController` (W6)

| Method | Route | Description | Auth |
|---|---|---|---|
| `POST` | `/api/assistant/ask` | Internal RAG over `docs/`, `ops/runbooks/`, `templates/skills/`; returns cited answers (file + line range) | JWT (any role — W6 is for internal agent + onboarding use) |

> **Why public to all roles:** W6 is intended for the Infraspekt agents and
> human engineers. Locking it behind a separate role complicates the agent loop.
> A future spec can restrict by role if abuse is observed.

### `IntentAdvisoryMiddleware` (W4)

The advisory middleware runs **after** JWT validation and **before** the routing
controller. It calls FastAPI's `/v1/intent/classify` (≤ 80ms p95) and tags the
`HttpContext` with the routing decision, but the .NET-side **always** has a
hard-coded fallback route. The middleware is **never** the source of truth for
routing — it is observability + analytics only.

```csharp
public class IntentAdvisoryMiddleware
{
    public async Task InvokeAsync(HttpContext ctx, IFastAPIClient fastApi, RequestDelegate next)
    {
        // Only consult FastAPI for ambiguous routes
        if (!IsAmbiguousRoute(ctx.Request.Path))
        {
            await next(ctx);
            return;
        }

        try
        {
            var decision = await fastApi.ClassifyIntentAsync(
                new IntentClassificationRequest(ctx.Request.Body, ctx.User.FindFirst("sub")?.Value),
                ctx.RequestAborted);

            // Tag the HttpContext for the controller to read
            ctx.Items["kendo.intent.route"] = decision.Route;
            ctx.Items["kendo.intent.confidence"] = decision.Confidence;
        }
        catch (Exception)
        {
            // Fail open — the controller uses its hard-coded fallback route
            ctx.Items["kendo.intent.route"] = "fallback";
        }

        await next(ctx);
    }
}
```

> **Hard-coded fallback rule:** controllers MUST always have a default
> route (e.g., `POST /api/messages` defaults to `support.create` if
> `ctx.Items["kendo.intent.route"]` is `null` or `fallback`). A FastAPI
> outage degrades to "previous behavior," not to a 500.

### Internal — `EmbeddingAdminController` is on `UserService`, not the Gateway

The Gateway does **not** expose the admin write endpoint. FastAPI calls
`UserService`'s `EmbeddingAdminController` directly via `IFastAPIClient`'s
**second** implementation, `IFastAPIDataAdminClient` (out of scope for this
spec; added when W5 ships). For now, this spec authors the service-JWT
minter the day_20 spec's `EmbeddingAdminController` will validate.

---

## Implementation Plan (Commit Units)

### Unit 1 — `RsaKeyProvider` + `JwtOptions`
- **Files:** `src/Shared/Kendo.Shared.Authentication/RsaKeyProvider.cs`, `src/Shared/Kendo.Shared.Authentication/JwtOptions.cs`
- **Gate:** unit tests for key generation, persistence, and rotation
- **Commit:** `feat(auth): add RsaKeyProvider for RSA-2048 keypair generation, persistence, and rotation`

### Unit 2 — `AddKendoJwt` extension + `JwksEndpoint`
- **Files:** `src/Shared/Kendo.Shared.Authentication/AddKendoJwt.cs`, `src/Gateway/WellKnown/JwksEndpoint.cs`, `src/Gateway/Program.cs` (wired)
- **Gate:** integration test that mints a JWT, hits `/.well-known/jwks.json`, validates the JWT
- **Commit:** `feat(auth): add AddKendoJwt extension and /.well-known/jwks.json endpoint (CCD-2)`

### Unit 3 — `ServiceJwtMinter` (CCD-3)
- **Files:** `src/Shared/Kendo.Shared.Authentication/ServiceJwtMinter.cs`
- **Gate:** unit test that the minter produces a JWT with `scope: "admin:writes"`, `token_use: "service"`, and a 60s lifetime
- **Commit:** `feat(auth): add ServiceJwtMinter for short-lived admin:writes service JWTs (CCD-3)`

### Unit 4 — `AdminScopePolicies` (shared with day_20)
- **Files:** `src/Shared/Kendo.Shared.Authentication/AdminScopePolicies.cs`
- **Gate:** 3 unit tests (valid scope passes, missing scope → 403, user JWT with admin:writes → 403)
- **Commit:** `feat(auth): add admin:writes scope policy with service-JWT enforcement (CCD-3)`

### Unit 5 — `FastApiOptions` + `IFastAPIClient` + `FastApiClient`
- **Files:** `src/Shared/Kendo.Shared.Http/{FastApiOptions,IFastAPIClient,FastApiClient,FastApiExceptionMapper}.cs`, `src/Shared/Kendo.Shared.Http/AddKendoFastApiClient.cs`
- **Gate:** unit tests for the Polly policy (timeout, retry, circuit breaker), and for RFC 7807 mapping
- **Commit:** `feat(http): add IFastAPIClient with Polly timeout+retry+circuit-breaker (CCD-2, M5.5)`

### Unit 6 — `RagController` (W1, W2)
- **Files:** `src/Gateway/Controllers/RagController.cs`
- **Gate:** integration tests for `/api/events/ingest` (202 + Location), `/api/events/{id}/validate` (200), error paths
- **Commit:** `feat(events): add RagController for W1 (Event Ingestion RAG) and W2 (Event Validation)`

### Unit 7 — `UserSearchController` (W3)
- **Files:** `src/Gateway/Controllers/UserSearchController.cs`
- **Gate:** integration tests for `/api/users/search` returning IDs + relevance, `/api/users/search/{id}` resolving via `UserService`
- **Commit:** `feat(users): add UserSearchController for W3 (User Profile Semantic Search)`

### Unit 8 — `AssistantController` (W6)
- **Files:** `src/Gateway/Controllers/AssistantController.cs`
- **Gate:** integration test that the response includes `citations[]` with file + line range
- **Commit:** `feat(assistant): add AssistantController for W6 (Document Q&A)`

### Unit 9 — `IntentAdvisoryMiddleware` (W4)
- **Files:** `src/Gateway/Middleware/IntentAdvisoryMiddleware.cs`, `src/Gateway/Program.cs` (wired before MVC)
- **Gate:** chaos test: FastAPI down → middleware fails open → controller uses fallback route → no 500
- **Commit:** `feat(routing): add IntentAdvisoryMiddleware with hard-coded fallback (W4)`

### Unit 10 — Wire all new services + middleware in `Program.cs`
- **Files:** `src/Gateway/Program.cs`
- **Gate:** `dotnet build src/Gateway`; `dotnet test`; `docker compose up -d gateway` and `curl /.well-known/jwks.json`
- **Commit:** `feat(gateway): wire AddKendoJwt, AddKendoFastApiClient, AddKendoServiceJwtMinter, AddKendoAdminScopePolicies, IntentAdvisoryMiddleware`

### Unit 11 — Docker compose + .env updates
- **Files:** `docker-compose.yml`, `.env.example`
- **Gate:** `docker compose config` (validation)
- **Commit:** `chore(env): pass JWT and FastAPI env vars to gateway`

---

## Success Checklist

| # | Criterion | Maps to |
|---|-----------|---------|
| 1 | `RsaKeyProvider` generates a 2048-bit RSA keypair on first boot and persists it at the configured path | M0.1 + CCD-2 |
| 2 | `/.well-known/jwks.json` returns the public key in JWK format; FastAPI can resolve it | M0.1 + CCD-2 |
| 3 | `AddKendoJwt` rejects an expired, wrong-issuer, wrong-audience, or wrong-signature token with RFC 7807 `401` | M0.1 |
| 4 | `ServiceJwtMinter` produces a JWT with `scope: "admin:writes"`, `token_use: "service"`, 60s lifetime | M0.1 + CCD-3 |
| 5 | `admin:writes` policy accepts a service JWT and rejects a user JWT with the same scope (via `token_use` claim) | M0.1 + CCD-3 |
| 6 | `IFastAPIClient` registers a named HttpClient with Polly timeout, retry (transient-fault only), and circuit breaker | M0.2 + M5.5 |
| 7 | `FastAPIClient` maps FastAPI's RFC 7807 responses to the Gateway's `KendoProblemDetails` without shape change | M0.2 |
| 8 | `RagController` returns `202 Accepted` with a `Location` header on `POST /api/events/ingest` | M0.2 + W1 |
| 9 | `RagController` returns `200` with the validation result on `POST /api/events/{id}/validate` | M0.2 + W2 |
| 10 | `UserSearchController` returns top-N user IDs + relevance on `GET /api/users/search` | M0.2 + W3 |
| 11 | `AssistantController` returns cited answers (file + line range) on `POST /api/assistant/ask` | M0.2 + W6 |
| 12 | `IntentAdvisoryMiddleware` fails open to a hard-coded fallback route when FastAPI is down (chaos test) | M0.2 + W4 |
| 13 | All existing 97 unit tests still pass (regression guard) | Inherited |
| 14 | `docker compose up` starts all 6 services; gateway health check passes; `/.well-known/jwks.json` reachable | Integration validation |

---

## Resilience Mandate

The `FastAPIClient` is the first **service-to-service** HTTP caller in the platform.
It establishes the resilience policy that the Worker (W7) will mirror in
`day_19_spec.md` and that FastAPI-side mirrors with `pybreaker`+`tenacity` per
the FastAPI spec.

| Concern | .NET equivalent (this spec) | Python equivalent (FastAPI spec) |
|---|---|---|
| Timeout | Polly `TimeoutPolicy(FastApiOptions.TimeoutSeconds)` | `tenacity` + `asyncio.wait_for` + `httpx.Timeout` |
| Retry | Polly `RetryPolicy(3, exponential + jitter, transient-fault predicate)` | `tenacity` `@retry(wait_exponential_jitter, retry=retry_if_exception_type(...))` |
| Circuit breaker | Polly `CircuitBreakerPolicy(3 failures → 30s break)` | `pybreaker.CircuitBreaker(fail_max=3, reset_timeout=30)` |
| RFC 7807 | `FastApiExceptionMapper` translates FastAPI's `application/problem+json` to `KendoProblemDetails` | FastAPI exception handler emits `application/problem+json` with `trace_id` |
| Observability | OpenTelemetry `ActivitySource` with `kind=client`, `peer=fastapi:8000` | OpenTelemetry `opentelemetry-instrumentation-httpx` with `kind=client` |

**Independent circuit breaker per dependency** (Postgres, Azure OpenAI, FastAPI) is
the established pattern from Day 03. The `FastAPIClient` adds a **new** breaker
specifically for FastAPI failures — distinct from any DB or internal HTTP breaker.

> **W4 advisory failure mode:** `IntentAdvisoryMiddleware` MUST be wrapped in
> `try/catch` and MUST fail open to the hard-coded fallback. A FastAPI outage
> cannot 500 the Gateway. This is verified by the chaos test in Unit 12.

---

## Dependency Check

| Resource | Required? | Status |
|----------|-----------|--------|
| `RsaSecurityKey`, `AddJwtBearer` | Yes — JWT validation | Standard ASP.NET Core 8+; ✅ |
| `Polly` v8 | Yes — resilience pipeline | Already present (Day 03); ✅ |
| `KendoProblemDetails` | Yes — RFC 7807 error responses | Already present (Day 05); ✅ |
| `AddKendoObservability` | Yes — OpenTelemetry tracing | Already present (Day 04); ✅ |
| `Event`, `EventValidation`, `UserEmbedding` entities | Yes — `RagController` returns/accepts them | Owned by `day_20_spec.md` |
| `EventIngestedEvent`, `EventValidatedEvent` | Yes — `RagController` emits them after W1/W2 | Owned by `day_18_spec.md` |
| `AddKendoRebusAiProducer` | Yes — `RagController` publishes to `kendo-events-ai` | Owned by `day_18_spec.md` |
| `Kendo.Shared.Authentication.AdminScopePolicies` | No — Gateway does not enforce `admin:writes` (only UserService does) | Owned by `day_20_spec.md` + this spec (Unit 4) |
| `IFastAPIDataAdminClient` (W5) | No | Future spec |
| FastAPI service | No — endpoint contracts only | Owned by `fastapi_rag_service_spec.md` |

---

## Open Questions / Clarifications

- **`IssuerSigningKeyResolver` anti-pattern:** the spec uses
  `BuildServiceProvider()` inside the resolver for clarity. The final
  implementation should inject `IServiceProvider` via closure. Confirm this is
  acceptable as a draft, and that the dev cycle will refactor it.
- **JWKS endpoint authentication:** the endpoint is `[AllowAnonymous]` (anyone
  can read the public key — this is RFC-standard). Confirm acceptable.
- **Keypair storage:** the spec uses `secrets/jwt-private.pem` on the local
  filesystem. In production, this should be a Kubernetes Secret or Azure
  Key Vault. Confirm whether the team prefers a `IConfiguration`-bound
  `X509Certificate2` (cleaner for prod) or a PEM file (dev-friendly).
- **User JWT issuance:** the spec authors the **validation** side of JWTs but
  does not author the **issuance** side. Today, the project has no
  login/register endpoint. Confirm whether user JWTs are out of scope for
  this milestone (the Gateway validates tokens issued by an external IdP)
  or whether day_17 should also author a `/api/auth/login` endpoint. (The
  audit does not mention this; recommended: keep issuance out of scope
  for now.)
- **Service JWT target audience:** the `ServiceJwtMinter` mints tokens with
  `aud: "kendo.api"`. The `UserService` `EmbeddingAdminController` validates
  the same audience. Confirm this is the right shape (vs. a separate
  `aud: "kendo.api.internal"`).
- **Rotation trigger:** the spec has `KENDO__JWT__ROTATION_DAYS` but no
  scheduled task to actually trigger rotation. Confirm whether a
  `BackgroundService` should be added, or whether rotation is manual.

---

## Change Log

| Date | Author | Change |
|---|---|---|
| 2026-06-16 | Platform Architect (reconciliation) | Initial spec — Gateway JWT auth (M0.1, CCD-2) and FastAPI integration (M0.2, M5.5 parity) |
