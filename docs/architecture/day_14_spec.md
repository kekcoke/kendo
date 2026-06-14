# Architecture Specification — Day 14

## Milestone Scope
- **Milestone:** M3.4 — Rate limiting & shedding
- **Roadmap phase:** 03 — High Availability & Chaos Testing
- **Components touched:**
  - `src/Gateway/` — Program.cs (register rate limiting + load shedding middleware)
  - `src/Shared/` — new `RateLimiting/` directory with shared extensions and middleware
- **Acceptance criteria from roadmap (M3.4):**
  - [ ] Rate limiter returns `429 Too Many Requests` with `Retry-After` header above the configured threshold
  - [ ] Load shedding returns `503 Service Unavailable` with RFC 7807 body under simulated extreme concurrency
- **Explicitly out of scope:**
  - M3.5 Graceful shutdown (SIGTERM handler + drain timeout)
  - M3.6 Ops runbooks for each failure scenario (separate milestone)
  - Multi-replica rate limit state sharing (Redis-backed distributed rate limiting is deferred — single-instance fixed window is sufficient for this milestone)
  - NGINX-level rate limiting (handled at the application layer in Gateway)

---

## Context & Design Decisions

### Existing infrastructure relied upon
| Resource | Role for this milestone |
|---|---|
| `ProblemDetailsMiddleware` | Already handles RFC 7807 body serialization — load shedding 503 will flow through it |
| `KendoProblemDetails` | The `Status`, `Title`, `Detail`, `TraceId` fields map directly to our 503 response |
| `IResiliencePipeline` / `PollyResiliencePipeline` | Not used directly — rate limiting and load shedding use .NET's `System.Threading.RateLimiting` instead of Polly (rate limiting is a middleware concern, not a per-call resilience policy) |
| `appsettings.json` (Gateway) | Rate limiting + concurrency options will be externalized here |

### Why .NET built-in rate limiting instead of Polly?
- `System.Threading.RateLimiting` (now part of `Microsoft.AspNetCore.RateLimiting`) is the canonical .NET approach for HTTP rate limiting
- It provides `RateLimitMetadata` on `HttpContext` for logging Retry-After values
- It integrates with the ASP.NET Core middleware pipeline natively
- Polly is a resilience library for call-level retry/circuit-breaking — it doesn't handle HTTP 429 coordination

### Design constraints
- **No distributed rate limit state:** Single-instance fixed window. Distributed (Redis-backed) rate limiting is deferred to a future milestone.
- **Health endpoints exempt:** `/health/live` and `/health/ready` bypass both rate limiter and load shedder.
- **Middleware order:** `LoadSheddingMiddleware` runs after rate limiting but before `ProblemDetailsMiddleware` — 503 responses still flow through RFC 7807 serialization.

---

## Layer Changes

### `src/Shared/` — New RateLimiting module
```
src/Shared/RateLimiting/
├── RateLimitingServiceCollectionExtensions.cs   # AddKendoRateLimiting() — registers RateLimiter + policy
├── LoadSheddingMiddleware.cs                     # Concurrency limiter middleware — 503 when saturated
├── RateLimitingOptions.cs                        # Configuration model for fixed-window + concurrency
```

### `src/Gateway/` — Wiring changes only
- `Program.cs` — add `builder.Services.AddKendoRateLimiting()` + `app.UseRateLimiter()` registration
- `appsettings.json` — add `RateLimiting` config section

### No changes to:
- `src/UserService/` — not in scope
- `src/Worker/` — not in scope
- `ops/nginx/nginx.conf` — not in scope
- `tests/` — new test file only for rate limiting + load shedding

---

## Data Contracts

### Rate limiting — 429 response
```json
// application/problem+json (RFC 7807)
{
  "type": "https://httpstatuses.com/429",
  "title": "Too Many Requests",
  "status": 429,
  "detail": "Rate limit exceeded. Please retry after the Retry-After period.",
  "instance": "/api/users",
  "traceId": "f47ac10b-58cc-4372-a567-0e02b2c3d479"
}
```
The `Retry-After` header is set by the `RateLimiter` middleware automatically from the `RateLimitLease.Metadata.RetryAfter` value.

### Load shedding — 503 response
```json
// application/problem+json (RFC 7807)
{
  "type": "https://httpstatuses.com/503",
  "title": "Service Unavailable",
  "status": 503,
  "detail": "Server is at maximum capacity. Please retry later.",
  "instance": "/api/users",
  "traceId": "f47ac10b-58cc-4372-a567-0e02b2c3d479"
}
```
The `Retry-After` header is **not** set on 503 — load shedding is an instantaneous capacity decision, not a rate window. (Can be added in a future enhancement.)

### Configuration schema (appsettings.json Gateway)
```json
{
  "RateLimiting": {
    "FixedWindow": {
      "PermitLimit": 100,
      "WindowSeconds": 60,
      "QueueProcessingOrder": "OldestFirst",
      "QueueLimit": 10
    },
    "Concurrency": {
      "MaxConcurrency": 50,
      "QueueProcessingOrder": "OldestFirst",
      "QueueLimit": 5
    }
  }
}
```

- `PermitLimit`: Max requests per `WindowSeconds` per client IP  
- `WindowSeconds`: Time window length in seconds  
- `MaxConcurrency`: Max concurrent in-flight requests before 503 is returned  
- `QueueLimit`: How many requests to queue before rejecting (0 = reject immediately)  

---

## Implementation Plan (Commit Units)

### Unit 1 — Rate limiting options & service registration in Kendo.Shared

- **Files:**
  - `src/Shared/RateLimiting/RateLimitingOptions.cs` (new)
  - `src/Shared/RateLimiting/RateLimitingServiceCollectionExtensions.cs` (new)
- **Gate command:** `dotnet test --filter "FullyQualifiedName~Unit1|FullyQualifiedName~RateLimitingServiceCollectionExtensions" --no-restore 2>&1 | tail -20`
- **Commit message:** `feat(shared): add RateLimitingOptions + AddKendoRateLimiting() registration`

#### RateLimitingOptions.cs
```csharp
namespace Kendo.Shared.RateLimiting;

public class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public FixedWindowOptions FixedWindow { get; set; } = new();
    public ConcurrencyOptions Concurrency { get; set; } = new();
}

public class FixedWindowOptions
{
    public int PermitLimit { get; set; } = 100;
    public int WindowSeconds { get; set; } = 60;
    public string QueueProcessingOrder { get; set; } = "OldestFirst";
    public int QueueLimit { get; set; } = 10;
}

public class ConcurrencyOptions
{
    public int MaxConcurrency { get; set; } = 50;
    public string QueueProcessingOrder { get; set; } = "OldestFirst";
    public int QueueLimit { get; set; } = 5;
}
```

#### RateLimitingServiceCollectionExtensions.cs
```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.RateLimiting;

namespace Kendo.Shared.RateLimiting;

public static class RateLimitingServiceCollectionExtensions
{
    public static IServiceCollection AddKendoRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<RateLimitingOptions>(
            configuration.GetSection(RateLimitingOptions.SectionName));

        services.AddRateLimiter(limiterOptions =>
        {
            var opts = configuration
                .GetSection(RateLimitingOptions.SectionName)
                .Get<RateLimitingOptions>() ?? new();

            // Global fixed-window policy — keyed by client IP
            limiterOptions.AddFixedWindowLimiter(
                policyName: "FixedWindow",
                fixedWindowOptions =>
                {
                    fixedWindowOptions.PermitLimit = opts.FixedWindow.PermitLimit;
                    fixedWindowOptions.Window = TimeSpan.FromSeconds(opts.FixedWindow.WindowSeconds);
                    fixedWindowOptions.QueueProcessingOrder =
                        Enum.Parse<QueueProcessingOrder>(opts.FixedWindow.QueueProcessingOrder);
                    fixedWindowOptions.QueueLimit = opts.FixedWindow.QueueLimit;
                });

            // Global — 429 responses: set Retry-After, log, return RFC 7807 body
            limiterOptions.OnRejected = async (context, cancellationToken) =>
            {
                var retryAfter = context.Lease.GetAllMetadata()
                    .FirstOrDefault(m => m.Key == "RETRY_AFTER").Value is TimeSpan ts
                    ? ts.TotalSeconds.ToString("F0")
                    : null;

                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.Headers.RetryAfter = retryAfter;

                var traceId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "unknown";
                var problemDetails = new
                {
                    type = "https://httpstatuses.com/429",
                    title = "Too Many Requests",
                    status = 429,
                    detail = "Rate limit exceeded. Please retry after the Retry-After period.",
                    instance = context.HttpContext.Request.Path.Value,
                    traceId
                };

                context.HttpContext.Response.ContentType = "application/problem+json; charset=utf-8";
                await System.Text.Json.JsonSerializer.SerializeAsync(
                    context.HttpContext.Response.Body, problemDetails, cancellationToken: cancellationToken);
            };
        });

        return services;
    }
}
```

- **Package dependency:** Add `Microsoft.AspNetCore.RateLimiting` to `Kendo.Shared.csproj`

---

### Unit 2 — Load Shedding middleware in Kendo.Shared

- **Files:**
  - `src/Shared/RateLimiting/LoadSheddingMiddleware.cs` (new)
- **Gate command:** `dotnet test --filter "FullyQualifiedName~Unit2|FullyQualifiedName~LoadSheddingMiddleware" --no-restore 2>&1 | tail -20`
- **Commit message:** `feat(shared): add LoadSheddingMiddleware — concurrency limiter returns 503 RFC 7807`

#### LoadSheddingMiddleware.cs
```csharp
using System.Threading.RateLimiting;
using Kendo.Shared.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kendo.Shared.RateLimiting;

/// <summary>
/// Middleware that limits concurrent in-flight requests using a semaphore-based
/// <see cref="ConcurrencyLimiter"/>. When the limit is exceeded, returns 503
/// with an RFC 7807 Problem Details body.
///
/// Always bypassed for health check endpoints (/health/live, /health/ready).
/// </summary>
public class LoadSheddingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ConcurrencyLimiter _limiter;
    private readonly ILogger<LoadSheddingMiddleware> _logger;
    private static readonly PathString HealthLivePath = new("/health/live");
    private static readonly PathString HealthReadyPath = new("/health/ready");

    public LoadSheddingMiddleware(
        RequestDelegate next,
        IOptions<RateLimitingOptions> options,
        ILogger<LoadSheddingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        var opts = options.Value.Concurrency;

        _limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = opts.MaxConcurrency,
            QueueProcessingOrder = Enum.Parse<QueueProcessingOrder>(opts.QueueProcessingOrder),
            QueueLimit = opts.QueueLimit
        });
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Bypass health endpoints
        if (context.Request.Path.StartsWithSegments(HealthLivePath, StringComparison.OrdinalIgnoreCase) ||
            context.Request.Path.StartsWithSegments(HealthReadyPath, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        using var lease = await _limiter.AcquireAsync(
            permitCount: 1,
            cancellationToken: context.RequestAborted);

        if (lease.IsAcquired)
        {
            try
            {
                await _next(context);
            }
            finally
            {
                // lease is disposed, release happens automatically
            }
        }
        else
        {
            _logger.LogWarning(
                "Load shedding — max concurrency ({MaxConcurrency}) reached for {Method} {Path}",
                _limiter.GetStatistics().TotalQueuedCount,
                context.Request.Method,
                context.Request.Path);

            // Return 503 RFC 7807 — ProblemDetailsMiddleware will NOT re-process
            // because we set the body and status directly
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;

            var traceId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "unknown";
            var problemDetails = new
            {
                type = "https://httpstatuses.com/503",
                title = "Service Unavailable",
                status = 503,
                detail = "Server is at maximum capacity. Please retry later.",
                instance = context.Request.Path.Value,
                traceId
            };

            context.Response.ContentType = "application/problem+json; charset=utf-8";
            await System.Text.Json.JsonSerializer.SerializeAsync(
                context.Response.Body, problemDetails, cancellationToken: context.RequestAborted);
        }
    }
}
```

---

### Unit 3 — Wire rate limiting + load shedding into Gateway Program.cs

- **Files:**
  - `src/Gateway/Program.cs` — add registrations
  - `src/Gateway/appsettings.json` — add `RateLimiting` config section
- **Gate command:** `dotnet build src/Gateway/ 2>&1 | tail -10`
- **Commit message:** `feat(gateway): wire rate limiter + load shedder into middleware pipeline`

#### Changes to Program.cs:
1. Add `using Kendo.Shared.RateLimiting;` at top
2. After `builder.Services.AddKendoDistributedCache(builder.Configuration);`:
   ```csharp
   builder.Services.AddKendoRateLimiting(builder.Configuration);
   ```
3. Before `app.UseKendoErrorHandling();`:
   ```csharp
   app.UseLoadShedding();
   app.UseRateLimiter();
   ```

#### Changes to appsettings.json — add after Redis section:
```json
"RateLimiting": {
  "FixedWindow": {
    "PermitLimit": 100,
    "WindowSeconds": 60,
    "QueueProcessingOrder": "OldestFirst",
    "QueueLimit": 10
  },
  "Concurrency": {
    "MaxConcurrency": 50,
    "QueueProcessingOrder": "OldestFirst",
    "QueueLimit": 5
  }
}
```

- **Also create:** `src/Shared/RateLimiting/LoadSheddingApplicationBuilderExtensions.cs` for `UseLoadShedding()` extension method

---

### Unit 4 — Tests for rate limiting + load shedding

- **Files:**
  - `tests/Kendo.Tests/RateLimiting/RateLimiterTests.cs` (new)
  - `tests/Kendo.Tests/RateLimiting/LoadSheddingTests.cs` (new)
- **Gate command:** `dotnet test --filter "Category=Unit" 2>&1 | tail -20`
- **Commit message:** `test(gateway): add rate limiter + load shedding integration tests`

#### RateLimiterTests.cs
- Tests using `WebApplicationFactory` to hit Gateway's rate-limited endpoint:
  - Test that health endpoint bypasses rate limiter
  - Test that rate-limited endpoint returns 429 with Retry-After after exceeding limit
  - Test that response body is `application/problem+json` RFC 7807

#### LoadSheddingTests.cs  
- Tests using `WebApplicationFactory` with low concurrency limit:
  - Test that health endpoint bypasses load shedder
  - Test that concurrent requests beyond limit return 503
  - Test that response body is `application/problem+json` RFC 7807
  - Test that within-limit requests succeed normally

---

## Success Checklist
- [ ] Rate limiter returns 429 Too Many Requests with Retry-After header (map: roadmap AC "429 + Retry-After")
- [ ] Rate-limited response body is `application/problem+json` (map: RFC 7807 requirement)
- [ ] Load shedder returns 503 Service Unavailable with RFC 7807 body (map: roadmap AC "503 RFC 7807")
- [ ] Health endpoints bypass both rate limiter and load shedder (map: non-regression — health probes must never be rate-limited)
- [ ] Rate limiter and load shedder are configurable via `appsettings.json` (map: operational requirement)
- [ ] All 76 existing tests still pass (map: non-regression against Phase 01+02 acceptance criteria)

---

## Resilience Mandate

### Rate limiter
- **Circuit breaker:** N/A — rate limiting is a middleware concern that runs before the application pipeline
- **Fallback:** The `OnRejected` handler returns the 429 response inline — no further processing occurs
- **Failure mode:** If the `RateLimiter` cannot acquire a lease (limit exceeded), the request is rejected immediately with 429

### Load shedder
- **Circuit breaker:** N/A — the `ConcurrencyLimiter` is a semaphore, not a circuit breaker
- **Fallback:** When `lease.IsAcquired` is false, the middleware returns 503 directly — no further pipeline processing
- **Failure mode:** If the concurrency limit is hit, the request is rejected immediately with 503
- **Thread safety:** `ConcurrencyLimiter` is thread-safe and designed for concurrent access

### General
- Both rate limiter and load shedder are **stateless** at the instance level — no shared state between replicas
- Both are **bypassed for health endpoints** to prevent cascading health-check failures
- Middleware execution order: `LoadSheddingMiddleware` → `RateLimiter` → `ProblemDetailsMiddleware` → Controllers
