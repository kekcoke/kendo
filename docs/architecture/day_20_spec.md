# Architecture Spec — Day 20 — UserService: Event Domain + Embeddings

> **Milestone:** M0.6 — UserService Event domain + embeddings foundation  
> **Roadmap phase:** 00 — Pre-FastAPI Reconciliation (see roadmap header note)  
> **Date:** 2026-06-16  
> **Architect:** Platform Architect (Orchestrator-delegated)  
> **Depends on:** `docs/architecture/fastapi_rag_service_spec.md` § Reconciled Cross-Cutting Decisions (CCD-1, CCD-3)

---

## Why this spec exists

The FastAPI RAG spec (M5.7–M5.14) introduces 8 workloads that all read from or write to
`UserService`. Today, `UserService` has no `Event` entity, no embedding columns, no
admin write endpoint, and the `fastapi_ro` PostgreSQL role does not exist. The M5.x
workloads **cannot ship** without these foundations in place.

This spec authors the data-layer prerequisites for Phase 05. It is the **first** of the
four reconciliatory specs (day_20, day_18, day_17, day_19) and is authored first because
every other spec references the entities, migrations, and contracts defined here.

---

## Milestone Scope

- **Roadmap milestone:** M0.6 — UserService Event domain + embeddings foundation
- **Components touched:**
  - `src/UserService/Domain/Events/Event.cs` — new entity
  - `src/UserService/Domain/Events/EventValidation.cs` — new entity (W2 result)
  - `src/UserService/Domain/Users/UserEmbedding.cs` — new sidecar entity (W3, W5)
  - `src/UserService/Data/AppDbContext.cs` — DbSet registrations, vector column type
  - `src/UserService/Data/Migrations/...AddEventDomain.cs` — new composite migration
  - `src/UserService/Data/Migrations/...AddFastAPIReadOnlyRole.cs` — `fastapi_ro` role
  - `src/UserService/Data/Migrations/...AddEmbeddingAdminWriterRole.cs` — `userservice_writer` role
  - `src/UserService/Controllers/EventsController.cs` — new public controller
  - `src/UserService/Controllers/EmbeddingAdminController.cs` — new internal controller
  - `src/Shared/Kendo.Shared.Authentication/AdminScopeRequirement.cs` — auth policy
  - `docker-compose.yml` — pass new env vars
  - `.env.example` — `KENDO__EVENT_EMBEDDING__DIM` etc.
- **Explicitly out of scope:**
  - FastAPI service code (covered by `fastapi_rag_service_spec.md`)
  - Gateway clients (covered by `day_17_spec.md`)
  - Worker handlers (covered by `day_19_spec.md`)
  - Service Bus event contracts (covered by `day_18_spec.md`)
  - Any change to existing `User`, `IdempotencyRecord`, `DlqRecord`, `OutboxMessage`

---

## Layer Changes

| Layer | Service | Change |
|-------|---------|--------|
| Domain | UserService | `Event`, `EventValidation`, `UserEmbedding` entities |
| Data | UserService | `AppDbContext` DbSets, vector column types, filtered indexes |
| Migrations | UserService | 3 new EF Core migrations (composite entity migration + 2 role migrations) |
| Auth | Kendo.Shared | `AdminScopeRequirement` policy, `admin:writes` scope handler |
| API (public) | UserService | `EventsController` — `GET/POST /api/events`, `GET /api/events/{id}` |
| API (internal) | UserService | `EmbeddingAdminController` — `POST /internal/embeddings`, `POST /internal/events/{id}/reindex` |
| Configuration | docker-compose | New env: `KENDO__EVENT_EMBEDDING__DIM`, `KENDO__EVENT_EMBEDDING__MODEL` |

---

## Data Contracts

### Entity: `Event`

The `Event` entity is the new domain root for the Event Scheduling workflow. It is
stored in the existing `kendo_users` database (no new database).

```csharp
namespace Kendo.UserService.Domain.Events;

public class Event
{
    public Guid Id { get; set; }                          // PK
    public Guid CreatedByUserId { get; set; }             // FK -> users.id
    public string Title { get; set; } = string.Empty;     // required, max 200
    public string? Description { get; set; }              // optional, max 4000
    public DateTimeOffset StartsAt { get; set; }          // required
    public DateTimeOffset EndsAt { get; set; }            // required
    public string? Location { get; set; }                 // optional, max 500
    public int? Headcount { get; set; }                   // optional, > 0
    public string? DietaryNotes { get; set; }             // optional, max 1000
    public string SourceText { get; set; } = string.Empty;// raw free-form text from W1
    public EventIngestionStatus IngestionStatus { get; set; } = EventIngestionStatus.Pending;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Navigation
    public User CreatedBy { get; set; } = null!;
    public EventValidation? Validation { get; set; }
}

public enum EventIngestionStatus { Pending, Validated, Rejected, Failed }
```

### Entity: `EventValidation` (W2 result, 1:1 with `Event`)

```csharp
namespace Kendo.UserService.Domain.Events;

public class EventValidation
{
    public Guid Id { get; set; }                  // PK
    public Guid EventId { get; set; }             // FK -> events.id, UNIQUE
    public bool Ok { get; set; }
    public string ConflictsJson { get; set; } = "[]";   // serialized list of conflict objects
    public string SuggestionsJson { get; set; } = "[]"; // serialized list of suggestions
    public string? ReasoningTrace { get; set; }   // optional, the W2 agent's reasoning trace
    public DateTimeOffset ValidatedAt { get; set; }

    public Event Event { get; set; } = null!;
}
```

### Entity: `UserEmbedding` (W3, W5 — sidecar table, not a column on `User`)

> **Why sidecar, not a column on `users`:** a column on the existing `users` table
> would force a schema change to the table the current Create-User workflow already
> depends on. The sidecar keeps `users` immutable, lets embedding writes be a
> separate transaction (or async job), and lets us drop the sidecar entirely
> without affecting the user domain.

```csharp
namespace Kendo.UserService.Domain.Users;

public class UserEmbedding
{
    public Guid UserId { get; set; }              // PK + FK -> users.id
    public string ModelName { get; set; } = string.Empty; // e.g. "BGE-large-en-v1.5"
    public int Dimensions { get; set; }            // 1024 (local) or 1536 (cloud)
    public Vector Embedding { get; set; } = null!; // pgvector type
    public DateTimeOffset EmbeddedAt { get; set; }

    public User User { get; set; } = null!;
}
```

### Vector column configuration (CCD-1 split: local vs cloud)

The vector dimension is **environment-driven**, not hardcoded. The active
dimension is read from `KENDO__EVENT_EMBEDDING__DIM` (default `1024` for local
dev, `1536` for cloud). The EF Core model binds the column type at startup so
the migration matches the active environment.

```csharp
// AppDbContext.OnModelCreating
modelBuilder.Entity<EventEmbedding>(b =>
{
    b.ToTable("event_embeddings");
    b.HasKey(e => e.EventId);
    b.Property(e => e.Embedding)
        .HasColumnType("vector")  // dimension is enforced by Postgres check, not EF
        .IsRequired();
    b.HasIndex(e => e.ModelName);
});

modelBuilder.Entity<UserEmbedding>(b =>
{
    b.ToTable("user_embeddings");
    b.HasKey(e => e.UserId);
    b.Property(e => e.Embedding).HasColumnType("vector").IsRequired();
    b.HasIndex(e => e.ModelName);
});
```

> **Dimension enforcement:** the `vector` column is created **untyped** (`vector`
> with no dimension suffix) so the same migration runs in local and cloud. A
> Postgres `CHECK` constraint on each table enforces the active dimension:

```sql
-- Applied by startup hook (not by EF migration, to keep migrations env-agnostic)
ALTER TABLE event_embeddings
  ADD CONSTRAINT event_embeddings_dim_check
  CHECK (vector_dims(embedding) = current_setting('kendo.embedding_dim')::int);
```

The `kendo.embedding_dim` setting is set by `Program.cs` on startup from
`KENDO__EVENT_EMBEDDING__DIM`. The check uses `current_setting` so the constraint
is dimension-agnostic and the value is controlled by application code, not the
migration.

### Migrations

| Migration | Adds | Reversible |
|---|---|---|
| `..._AddEventDomain` | `events`, `event_validations`, `event_embeddings`, `user_embeddings` tables + indexes | Yes |
| `..._AddFastAPIReadOnlyRole` | `fastapi_ro` role + grants (SELECT on relevant tables) | Yes |
| `..._AddEmbeddingAdminWriterRole` | `userservice_writer` role + grants (INSERT/UPDATE on `event_embeddings`, `events` via `EmbeddingAdminController` connection only) | Yes |

`fastapi_ro` role grants:

```sql
CREATE ROLE fastapi_ro LOGIN PASSWORD :'fastapi_ro_pw';
GRANT CONNECT ON DATABASE kendo_users TO fastapi_ro;
GRANT USAGE ON SCHEMA public TO fastapi_ro;
GRANT SELECT ON event_embeddings, user_embeddings, events, event_validations TO fastapi_ro;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON TABLES TO fastapi_ro;
```

`userservice_writer` role grants:

```sql
CREATE ROLE userservice_writer LOGIN PASSWORD :'userservice_writer_pw';
GRANT CONNECT ON DATABASE kendo_users TO userservice_writer;
GRANT USAGE ON SCHEMA public TO userservice_writer;
GRANT SELECT, INSERT, UPDATE ON event_embeddings TO userservice_writer;
GRANT SELECT, INSERT, UPDATE ON events, event_validations TO userservice_writer;
```

> **Two roles, two connection strings, one service.** `UserService` itself
> continues to use its existing app role for normal CRUD. The
> `EmbeddingAdminController` opens a **scoped `Npgsql` connection** as
> `userservice_writer` for the duration of a single request, then disposes.
> This is the second layer of defense-in-depth behind the JWT-scope check
> (CCD-3).

### Public API — `EventsController`

Standard .NET MVC controller. JWT-authenticated, RFC 7807 errors, OpenTelemetry traced.

| Method | Route | Description | Auth |
|---|---|---|---|
| `GET` | `/api/events` | List events for current user (paginated) | JWT (any role) |
| `GET` | `/api/events/{id}` | Get event by id | JWT (any role, owner only) |
| `POST` | `/api/events` | Create event (called by Gateway after W1 returns structured JSON) | JWT (any role) |
| `PATCH` | `/api/events/{id}` | Update event (owner only) | JWT (owner) |
| `DELETE` | `/api/events/{id}` | Soft delete (sets `DeletedAt`) | JWT (owner) |

> **Why the Gateway does the W1 call, not the controller:** W1 is an LLM call.
> Controllers must remain fast. The Gateway calls FastAPI, validates the
> structured JSON against the `Event` schema, then `POST`s to `UserService`. This
> keeps the controller's p95 < 50ms.

### Internal API — `EmbeddingAdminController`

Auth: **JWT with `admin:writes` scope claim** (CCD-3, option C). RFC 7807 errors.
OpenTelemetry traced. Route prefix `/internal`.

| Method | Route | Description | Auth |
|---|---|---|---|
| `POST` | `/internal/embeddings` | Upsert embedding for an event or user | JWT (`admin:writes` scope) |
| `POST` | `/internal/events/{id}/reindex` | Re-trigger embedding compute for an event (W5) | JWT (`admin:writes` scope) |

Request schema — `POST /internal/embeddings`:

```json
{
  "target": "event" | "user",
  "target_id": "uuid",
  "model_name": "string",
  "dimensions": 1024,
  "embedding": [0.0123, -0.0456, ...]
}
```

Response: `204 No Content` on success; RFC 7807 on failure.

> **Connection scoping:** the controller opens a `Npgsql` connection as
> `userservice_writer` for the duration of the request, executes the upsert in
> a transaction, and disposes. It does **not** register `userservice_writer` as
> the service's default connection role.

---

## Authentication — `admin:writes` scope (CCD-3, option C)

The scope is checked using a custom authorization policy in
`Kendo.Shared.Authentication`:

```csharp
namespace Kendo.Shared.Authentication;

public static class AdminScopePolicies
{
    public const string AdminWrites = "admin:writes";

    public static IServiceCollection AddKendoAdminScopePolicies(this IServiceCollection services)
    {
        services.AddAuthorization(opts =>
        {
            opts.AddPolicy(AdminWrites, p => p
                .RequireAuthenticatedUser()
                .RequireClaim("scope", AdminWrites)
                .RequireClaim("token_use", "service"));  // service JWTs only
        });
        return services;
    }
}
```

The `token_use: service` claim is set by the Gateway's service-JWT minter
(authored in `day_17_spec.md`) and is **never** present in user JWTs. This
prevents a regular user from being granted `admin:writes` by misconfiguration.

`EmbeddingAdminController` is decorated with `[Authorize(Policy = AdminScopePolicies.AdminWrites)]`.

---

## Implementation Plan (Commit Units)

### Unit 1 — Entities + DbContext
- **Files:** `Domain/Events/Event.cs`, `Domain/Events/EventValidation.cs`, `Domain/Users/UserEmbedding.cs`, `Data/AppDbContext.cs`
- **Gate:** `dotnet build src/UserService`
- **Commit:** `feat(data): add Event, EventValidation, UserEmbedding entities and DbContext registrations`

### Unit 2 — Composite migration `AddEventDomain`
- **Files:** `Data/Migrations/..._AddEventDomain.cs` (auto-generated)
- **Gate:** `dotnet ef migrations add AddEventDomain --project src/UserService && dotnet ef database update --project src/UserService`
- **Commit:** `feat(data): add AddEventDomain migration for events, event_validations, event_embeddings, user_embeddings`

### Unit 3 — `AddFastAPIReadOnlyRole` migration
- **Files:** `Data/Migrations/..._AddFastAPIReadOnlyRole.cs`
- **Gate:** integration test that confirms an `INSERT` from a `fastapi_ro` connection fails with `permission denied`
- **Commit:** `feat(data): add fastapi_ro PostgreSQL role with SELECT-only grants`

### Unit 4 — `AddEmbeddingAdminWriterRole` migration
- **Files:** `Data/Migrations/..._AddEmbeddingAdminWriterRole.cs`
- **Gate:** integration test that confirms `userservice_writer` can INSERT on `event_embeddings` and cannot DELETE on `events`
- **Commit:** `feat(data): add userservice_writer PostgreSQL role for EmbeddingAdminController`

### Unit 5 — `AdminScopePolicies` + JWT scope claim validation
- **Files:** `src/Shared/Kendo.Shared.Authentication/AdminScopePolicies.cs`, `src/Shared/Kendo.Shared.Authentication/AdminScopeRequirement.cs`, `src/Shared/Kendo.Shared.Authentication/AdminScopeHandler.cs`
- **Gate:** `dotnet test tests/Kendo.Tests --filter "Category=Auth"` (3 new tests: valid scope passes, missing scope returns 403, user JWT with `admin:writes` but no `token_use=service` returns 403)
- **Commit:** `feat(auth): add admin:writes scope policy and service-JWT enforcement`

### Unit 6 — `EventsController` (public)
- **Files:** `src/UserService/Controllers/EventsController.cs`
- **Gate:** `dotnet test tests/Kendo.Tests --filter "Category=Events"` (5 new tests: list, get, create, update, delete)
- **Commit:** `feat(events): add EventsController with GET/POST/PATCH/DELETE for the Event domain`

### Unit 7 — `EmbeddingAdminController` (internal) with scoped connection
- **Files:** `src/UserService/Controllers/EmbeddingAdminController.cs`, `src/UserService/Data/AdminWriterConnectionFactory.cs`
- **Gate:** `dotnet test tests/Kendo.Tests --filter "Category=EmbeddingAdmin"` (4 new tests: valid scope + valid payload → 204; valid scope + dimension mismatch → 422; invalid scope → 403; service JWT vs user JWT discrimination)
- **Commit:** `feat(events): add EmbeddingAdminController with admin:writes scope and scoped userservice_writer connection`

### Unit 8 — `KENDO__EVENT_EMBEDDING__DIM` startup hook (dimension check constraint)
- **Files:** `src/UserService/Program.cs` — adds startup hook that sets `kendo.embedding_dim` from config
- **Gate:** integration test that confirms inserting a 1536-dim vector into a 1024-dim-configured instance fails the check constraint
- **Commit:** `feat(events): add kendo.embedding_dim startup hook for dimension enforcement`

### Unit 9 — Docker compose + .env updates
- **Files:** `docker-compose.yml`, `.env.example`
- **Gate:** `docker compose up -d userservice && docker compose exec userservice dotnet ... healthcheck`
- **Commit:** `chore(env): pass KENDO__EVENT_EMBEDDING__DIM and KENDO__EVENT_EMBEDDING__MODEL to userservice`

---

## Success Checklist

| # | Criterion | Maps to |
|---|-----------|---------|
| 1 | `events`, `event_validations`, `event_embeddings`, `user_embeddings` tables exist in `kendo_users` | M0.6 |
| 2 | `pgvector` extension is enabled (already ✅ since Day 02) and `vector` column types are present on `event_embeddings` and `user_embeddings` | M0.6 + CCD-1 |
| 3 | `fastapi_ro` role exists; an `INSERT` from a `fastapi_ro` connection fails with `permission denied` | M0.6 + M5.3 |
| 4 | `userservice_writer` role exists; INSERT on `event_embeddings` succeeds, DELETE on `events` fails | M0.6 + CCD-3 |
| 5 | `EventsController` exposes the 5 routes; integration tests for each pass | M0.6 |
| 6 | `EmbeddingAdminController` returns `204` on a valid request with `admin:writes` scope + `token_use=service` | M0.6 + CCD-3 |
| 7 | `EmbeddingAdminController` returns RFC 7807 `403` for a request with `admin:writes` but `token_use=user` | M0.6 + CCD-3 |
| 8 | `EmbeddingAdminController` returns RFC 7807 `403` for a request with no `admin:writes` scope | M0.6 + CCD-3 |
| 9 | Startup hook sets `kendo.embedding_dim`; inserting a wrong-dimension vector fails the check constraint | CCD-1 |
| 10 | All existing 97 unit tests still pass (regression guard) | Inherited |
| 11 | `docker compose up` starts all 6 services (gateway, userservice, worker, postgres, redis, nginx); all health checks pass | Integration validation |

---

## Resilience Mandate

**N/A — this spec is data-layer only.** No new HTTP endpoints, no new outbound calls.
The `EventsController` follows the existing `ProblemDetailsMiddleware` (Day 05) and
the existing Polly pipeline (Day 03). The `EmbeddingAdminController` is JWT-authenticated
and uses the existing `AddKendoObservability` (Day 04). No new resilience policy is
introduced.

The outbound resilience for the Gateway → FastAPI call lives in
`day_17_spec.md` (Gateway). The FastAPI-side resilience lives in the FastAPI spec.

---

## Dependency Check

| Resource | Required? | Status |
|----------|-----------|--------|
| `Kendo.Shared.Authentication` | Yes — `AdminScopePolicies` lives here | New in Unit 5; minimal namespace, no breaking changes to existing `Kendo.Shared` consumers |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | Yes — vector type binding | Already present (Day 02) |
| `pgvector` extension | Yes — column type | Already present (Day 02) |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | Yes — for the `admin:writes` scope check | **OPEN:** the audit shows JWT config is entirely absent from the Gateway. This spec consumes JWT config authored in `day_17_spec.md`. If day_17 is not yet merged, day_20 cannot ship. |
| `Kendo.Shared.ProblemDetails` | Yes — for the new controllers' error responses | Already present (Day 05) |
| `Kendo.Shared.Observability` | Yes — for tracing the new controllers | Already present (Day 04) |
| Gateway | No — for the actual `IFastAPIClient` call | Owned by `day_17_spec.md` |
| FastAPI | No | Owned by `fastapi_rag_service_spec.md` |
| Worker | No | Owned by `day_19_spec.md` |

---

## Open Questions / Clarifications

- **Embedding dimension enforcement via `current_setting`:** this is a clean
  dimension-agnostic constraint, but it requires the startup hook to set the
  GUC before any insert. Confirm this is acceptable (vs. baking the dimension
  into the migration and requiring two migrations for local vs cloud).
- **Sidecar vs column for `UserEmbedding`:** recommended sidecar. Confirm.
- **`token_use` claim:** proposed as `service` for service JWTs. Confirm
  whether the existing user JWT (issued by future Gateway JWT minter) uses a
  different claim value (e.g. `user`) and whether `day_17_spec.md` will use a
  consistent naming convention.
- **Outbox on `Event` creation:** should the W1 → `Event` write go through the
  existing transactional outbox (Day 10)? Recommended: yes, so the
  `EventCreatedEvent` (defined in `day_18_spec.md`) is published atomically
  with the row insert. Confirm.
- **Soft delete column on `Event`:** `DeletedAt` is referenced in the
  `DELETE` route description but not in the entity. Confirm whether the spec
  should add a `DeletedAt DateTimeOffset?` property to the entity, or use a
  hard delete for now.

---

## Change Log

| Date | Author | Change |
|---|---|---|
| 2026-06-16 | Platform Architect (reconciliation) | Initial spec — UserService Event domain + embeddings foundation (M0.6) |
