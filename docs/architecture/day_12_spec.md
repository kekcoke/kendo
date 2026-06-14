# Day 12 — Architecture Specification

> **Session:** Day 12 | **Milestone:** M3.2 — Stateless validation  
> **Phase plan:** 03 — High Availability & Chaos Testing  
> **Author:** Platform Architect (Orchestrator-routed)  
> **Date:** 2026-06-14

---

## Milestone Scope

- **Milestone:** M3.2 — Stateless validation: sticky sessions disabled; any session state externalized to Redis or the database.
- **Roadmap phase:** 03 — High Availability & Chaos Testing
- **Components touched:**
  1. `Stateless Services` — all 3 services (Gateway, UserService, Worker)
  2. `Redis (or equivalent)` — external distributed cache / session store
- **Explicitly out of scope:**
  - M3.3 — Chaos suite (automated DB downtime, service crash, network partition tests)
  - M3.4 — Rate limiting & load shedding (429 + 503 enforcement at Gateway)
  - M3.5 — Graceful shutdown (SIGTERM handlers)
  - M3.6 — Ops runbooks (recovery playbooks for each failure scenario)

### Rationale

M3.1 (prior Day 12 session) established the NGINX reverse proxy with round-robin load balancing across 3 replicas per service. Sticky sessions are **already disabled** by default: NGINX uses DNS round-robin via variable-based `proxy_pass` (no `ip_hash`, no `sticky` directive). M3.2 formalises this posture and introduces Redis as the externalised state/cache store:

1. **Replica identity** — each container replica needs a unique identifier so operators and integration tests can verify traffic is truly round-robin. Docker `$HOSTNAME` provides this.
2. **Redis distributed cache** — Wire `StackExchange.Redis` + `IDistributedCache` into Kendo.Shared so any future session-like state is externalised immediately. Redis is already defined in the dependency map (Day 00) but was never wired.
3. **Middleware** — Add `X-Kendo-Replica` response header on every service response, confirming which replica served the request.
4. **Docker Compose** — Add Redis connection strings and expose the Redis service to all app services.

---

## Layer Changes

| Service/Project | Change |
|---|---|
| `Kendo.Shared` | Add `DistributedCacheServiceCollectionExtensions` — registers `StackExchange.Redis` + `IDistributedCache` via `AddKendoDistributedCache()`. Add `ReplicaIdentityMiddleware` — reads `$HOSTNAME` (or `HOSTNAME` env var), appends `X-Kendo-Replica` header to all responses. |
| `Kendo.Shared.csproj` | Add `Microsoft.Extensions.Caching.StackExchangeRedis` package reference. |
| `Gateway` (`Program.cs`) | Call `AddKendoDistributedCache()` + `UseKendoReplicaIdentity()`. Add `Redis__ConnectionString` to `appsettings.json`. |
| `UserService` (`Program.cs`) | Call `AddKendoDistributedCache()` + `UseKendoReplicaIdentity()`. Add `Redis__ConnectionString` to `appsettings.json`. |
| `Worker` (`Program.cs`) | Call `AddKendoDistributedCache()` + `UseKendoReplicaIdentity()`. Add `Redis__ConnectionString` to `appsettings.json`. |
| `docker-compose.yml` | Add `Redis__ConnectionString` env var to all 3 services (`redis:6379`). Add Redis service definition. |
| `ops/nginx/nginx.conf` | Add `X-Kendo-Replica` to `proxy_set_header` passthrough. |
| `tests/Kendo.Tests` | Add `ReplicaIdentityMiddlewareTests.cs` + `DistributedCacheRegistrationTests.cs`. |

---

## Data Contracts

### Redis Connection String (environment variable)

```yaml
Redis__ConnectionString: "redis:6379"
```

Applied via Docker Compose environment variables. Graceful fallback: if the connection string is missing or Redis is unreachable, `IDistributedCache` operations log a warning and degrade gracefully (no crash, no cascade).

### X-Kendo-Replica Response Header

| Header | Value | Type | Example |
|---|---|---|---|
| `X-Kendo-Replica` | Docker container hostname | `string` | `"gateway-1"`, `"gateway-2"`, `"gateway-3"` |

Every public HTTP response from Gateway, UserService, and Worker includes this header. NGINX passes it through from the upstream replica to the external caller.

### Replica Identity Resolution Algorithm

```
1. Read HOSTNAME environment variable (set by Docker to container ID/name)
2. If null/empty → fall back to Environment.MachineName
3. If still null/empty → "unknown"
4. Value is cached in a static Lazy<string> for the process lifetime
```

---

## Implementation Plan (Commit Units)

### Unit 1 — Kendo.Shared: Distributed cache registration + replica identity middleware

**Files:**
- `src/Shared/Caching/DistributedCacheServiceCollectionExtensions.cs` (new)
- `src/Shared/Caching/ReplicaIdentityMiddleware.cs` (new)
- `src/Shared/Kendo.Shared.csproj` (add package ref)

**Gate command:** `dotnet build src/Shared/Kendo.Shared.csproj --no-restore 2>&1 | tail -5`

**Commit message:**
```
feat(shared): add Redis distributed cache registration and replica identity middleware

Day 12 — unit 1 of 6 | Milestone M3.2
Coverage: ~
Lint: ~
```

**DistributedCacheServiceCollectionExtensions.cs** — registers `StackExchange.Redis` + `IDistributedCache` conditionally:
```csharp
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kendo.Shared.Caching;

public static class DistributedCacheServiceCollectionExtensions
{
    public static IServiceCollection AddKendoDistributedCache(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration["Redis__ConnectionString"]
                               ?? configuration["RedisConnectionStrings__DefaultConnection"];

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = connectionString;
                options.InstanceName = "kendo:";
            });
        }
        else
        {
            // Fallback: in-memory cache when Redis is not configured (local dev / CI)
            services.AddDistributedMemoryCache();
        }

        return services;
    }
}
```

**ReplicaIdentityMiddleware.cs** — reads HOSTNAME, appends header:
```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Kendo.Shared.Caching;

public class ReplicaIdentityMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _replicaId;
    private readonly ILogger<ReplicaIdentityMiddleware> _logger;

    public ReplicaIdentityMiddleware(RequestDelegate next, ILogger<ReplicaIdentityMiddleware> logger)
    {
        _next = next;
        _logger = logger;

        _replicaId = Environment.GetEnvironmentVariable("HOSTNAME")
                     ?? Environment.MachineName
                     ?? "unknown";

        _logger.LogInformation("Replica identity resolved: {ReplicaId}", _replicaId);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            if (!context.Response.Headers.ContainsKey("X-Kendo-Replica"))
            {
                context.Response.Headers["X-Kendo-Replica"] = _replicaId;
            }
            return Task.CompletedTask;
        });

        await _next(context);
    }
}

public static class ReplicaIdentityMiddlewareExtensions
{
    public static IApplicationBuilder UseKendoReplicaIdentity(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ReplicaIdentityMiddleware>();
    }
}
```

**Kendo.Shared.csproj** — add after existing OpenTelemetry reference:
```xml
<PackageReference Include="Microsoft.Extensions.Caching.StackExchangeRedis" Version="10.0.9" />
```

---

### Unit 2 — Gateway: wire middleware + Redis connection

**Files:**
- `src/Gateway/Program.cs` (insert 2 lines)
- `src/Gateway/appsettings.json` (add `Redis` section)

**Gate command:** `dotnet build src/Gateway/Kendo.Gateway.csproj --no-restore 2>&1 | tail -5`

**Commit message:**
```
feat(gateway): wire replica identity middleware and Redis distributed cache

Day 12 — unit 2 of 6 | Milestone M3.2
Coverage: ~
Lint: ~
```

**Program.cs changes:**
- After `builder.Services.AddKendoObservability(...)` → add `builder.Services.AddKendoDistributedCache(builder.Configuration);`
- After `app.UseKendoErrorHandling()` → add `app.UseKendoReplicaIdentity();`

**appsettings.json additions** (append after closing brace of Resilience):
```json
  "Redis": {
    "ConnectionString": "localhost:6379"
  }
```

---

### Unit 3 — UserService: wire middleware + Redis connection

**Files:**
- `src/UserService/Program.cs` (insert 2 lines)
- `src/UserService/appsettings.json` (add `Redis` section)

**Gate command:** `dotnet build src/UserService/Kendo.UserService.csproj --no-restore 2>&1 | tail -5`

**Commit message:**
```
feat(userservice): wire replica identity middleware and Redis distributed cache

Day 12 — unit 3 of 6 | Milestone M3.2
Coverage: ~
Lint: ~
```

**Program.cs changes:**
- After `builder.Services.AddKendoObservability(...)` → add `builder.Services.AddKendoDistributedCache(builder.Configuration);`
- After `app.UseKendoErrorHandling()` → add `app.UseKendoReplicaIdentity();`

**appsettings.json additions:**
```json
  "Redis": {
    "ConnectionString": "localhost:6379"
  }
```

---

### Unit 4 — Worker: wire middleware + Redis connection

**Files:**
- `src/Worker/Program.cs` (insert 2 lines)
- `src/Worker/appsettings.json` (add `Redis` section)

**Gate command:** `dotnet build src/Worker/Kendo.Worker.csproj --no-restore 2>&1 | tail -5`

**Commit message:**
```
feat(worker): wire replica identity middleware and Redis distributed cache

Day 12 — unit 4 of 6 | Milestone M3.2
Coverage: ~
Lint: ~
```

**Program.cs changes:**
- After `builder.Services.AddKendoObservability(...)` → add `builder.Services.AddKendoDistributedCache(builder.Configuration);`
- After `app.UseKendoErrorHandling()` → add `app.UseKendoReplicaIdentity();`

**appsettings.json additions:**
```json
  "Redis": {
    "ConnectionString": "localhost:6379"
  }
```

---

### Unit 5 — Docker Compose + NGINX: Redis service + replica header passthrough

**Files:**
- `docker-compose.yml` (add Redis service; add `Redis__ConnectionString` env vars to all 3 services)
- `ops/nginx/nginx.conf` (add `proxy_set_header X-Kendo-Replica $upstream_http_x_kendo_replica;`)

**Gate command:** `docker compose config 2>&1 | head -20`

**Commit message:**
```
chore(infra): add Redis service, connection strings, and replica header passthrough

Day 12 — unit 5 of 6 | Milestone M3.2
Coverage: ~
Lint: ~
```

**docker-compose.yml — Redis service** (new block):
```yaml
  redis:
    image: redis:7-alpine
    ports:
      - "6379:6379"
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 5s
      timeout: 3s
      retries: 3
      start_period: 5s
```

**docker-compose.yml** — add `Redis__ConnectionString` to services:
```yaml
    - Redis__ConnectionString=redis:6379
```

**nginx.conf** — add to `server` block alongside existing `proxy_set_header` directives:
```nginx
proxy_set_header X-Kendo-Replica $upstream_http_x_kendo_replica;
```

---

### Unit 6 — Tests: replica identity and cache registration

**Files:**
- `tests/Kendo.Tests/Caching/ReplicaIdentityMiddlewareTests.cs` (new)
- `tests/Kendo.Tests/Caching/DistributedCacheRegistrationTests.cs` (new)

**Gate command:** `dotnet test tests/Kendo.Tests --filter "Category=Unit|Category=Caching" 2>&1 | tail -15`

**Commit message:**
```
test(caching): add tests for replica identity middleware and cache registration

Day 12 — unit 6 of 6 | Milestone M3.2
Coverage: ~
Lint: ~
```

---

## Success Checklist

- [ ] Every HTTP response from Gateway, UserService, and Worker includes an `X-Kendo-Replica` header whose value matches the Docker container hostname.
- [ ] Redis distributed cache is registered in all 3 services; `IDistributedCache` is available to inject.
- [ ] When `Redis__ConnectionString` is missing (local dev/CI), `IDistributedCache` falls back to `AddDistributedMemoryCache()` — no crash.
- [ ] NGINX passes `X-Kendo-Replica` from upstream replica through to the external caller.
- [ ] NGINX has no `ip_hash`, `sticky`, or session-affinity directive — sticky sessions remain disabled.
- [ ] 94/94 existing unit tests + 5 infrastructure tests continue to pass.
- [ ] `docker compose up` starts all services + Redis; all `/health/ready` probes pass.

---

## Resilience Mandate

**Redis distributed cache:**
- Circuit breaker: N/A — `IDistributedCache` operations are best-effort. The cache is a performance optimisation, not a correctness dependency. If Redis is unreachable:
  1. Set operations log a warning and fail gracefully (catch `RedisConnectionException`, log warning, no rethrow).
  2. Get operations return `null` (cache miss) — callers must handle cache misses gracefully.
  3. No cascading failure: the application continues serving requests without Redis.
- The in-memory fallback (`AddDistributedMemoryCache()`) is the default when no Redis connection string is present.

**Replica identity:**
- N/A — no external dependencies. Reads `HOSTNAME` env var at startup, caches for process lifetime. No network calls, no DB access.

**Sticky session enforcement:**
- N/A — sticky sessions are permanently disabled by NGINX config (no `ip_hash`, no `sticky` cookie/session directive). This is a structural decision, not a code-enforced one.
