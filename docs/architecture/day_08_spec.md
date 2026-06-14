# Day 08 — Architecture Specification

## Milestone Scope

- **Milestone:** M2.3 — Background consumer: message consumer implemented in Worker Service; idempotency key enforced on every handler
- **Roadmap phase:** 02 — Asynchronous Decoupling
- **Components touched:**
  - `Worker` — new `UserCreatedEventHandler` (Rebus message handler), new `WorkerDbContext` with `IdempotencyRecords` table, EF Core packages and migration
  - `Kendo.Shared` — no changes (reuses existing `KendoMessage.MessageId` as idempotency key)
  - `UserService` — no changes to controllers or repository; user status is read/updated by Worker via `AppDbContext` (shared database)
- **Explicitly out of scope:**
  - M2.4 — Dead Letter Queue consumer and alerting
  - M2.5 — Transactional outbox pattern
  - M2.6 — `traceparent` propagation across producer and consumer

## Layer Changes

### Worker (`src/Worker/`)
- Add EF Core + Npgsql packages to `Kendo.Worker.csproj`
- New `Data/` directory containing:
  - `IdempotencyRecord.cs` — entity for tracking processed messages
  - `WorkerDbContext.cs` — EF Core DbContext for idempotency records (targets same `kendo_users` DB)
- New `Handlers/UserCreatedEventHandler.cs` — Rebus message handler with idempotency enforcement
- Update `Program.cs` — register `WorkerDbContext`, add migration step
- EF Core migration: add `IdempotencyRecords` table

### UserService (`src/UserService/`)
- **No changes** — the Worker reads/writes User entities via the shared database. Existing `UsersController`, `UserRepository`, and `AppDbContext` remain untouched.

### Kendo.Shared (`src/Shared/`)
- **No changes** — `KendoMessage.MessageId` (Guid) is reused as the idempotency key. No new shared types needed.

## Data Contracts

### Entity: `IdempotencyRecord`

| Column | Type | Constraints |
|---|---|---|
| `MessageId` | `uuid` | PK |
| `HandlerName` | `text` | NOT NULL — identifies which handler processed this message (e.g. `"UserCreated"`) |
| `Status` | `text` | NOT NULL, default `'Processing'`, values: `Processing`, `Completed`, `Failed` |
| `CreatedAt` | `timestamptz` | NOT NULL, default `now()` |
| `CompletedAt` | `timestamptz` | NULLABLE |

```csharp
namespace Kendo.Worker.Data;

public enum IdempotencyStatus
{
    Processing,
    Completed,
    Failed
}

public class IdempotencyRecord
{
    public Guid MessageId { get; set; }    // = KendoMessage.MessageId
    public string HandlerName { get; set; } = string.Empty;
    public IdempotencyStatus Status { get; set; } = IdempotencyStatus.Processing;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}
```

### WorkerDbContext

```csharp
namespace Kendo.Worker.Data;

public class WorkerDbContext : DbContext
{
    public WorkerDbContext(DbContextOptions<WorkerDbContext> options) : base(options) { }

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdempotencyRecord>(entity =>
        {
            entity.HasKey(e => e.MessageId);
            entity.Property(e => e.MessageId).ValueGeneratedNever(); // set by application
            entity.Property(e => e.HandlerName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .IsRequired()
                .HasDefaultValue(IdempotencyStatus.Processing);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.CompletedAt);
        });
    }
}
```

### Handler: `UserCreatedEventHandler`

```csharp
namespace Kendo.Worker.Handlers;

public class UserCreatedEventHandler : IHandleMessages<UserCreatedEvent>
{
    private readonly WorkerDbContext _db;
    private readonly AppDbContext _userDb;
    private readonly ILogger<UserCreatedEventHandler> _logger;

    // DI: WorkerDbContext + UserService's AppDbContext (shared DB, different DbContext types)
}
```

### Idempotency enforcement logic

```
OnMessage(UserCreatedEvent msg):
  1. BEGIN transaction (on WorkerDbContext connection)
  2. Try INSERT INTO IdempotencyRecords (MessageId, HandlerName, Status) VALUES (@msg.MessageId, 'UserCreated', 'Processing')
     - If unique constraint violation → existing record found:
       - If Status = 'Completed' → log duplicate, ROLLBACK, return
       - If Status = 'Processing' → crash recovery path:
           - Log recovery, proceed to step 3
     - If insert succeeds → first-time processing, proceed to step 3
  3. Lookup User by msg.UserId via AppDbContext
  4. If user.Status == Pending → update to Processing
  5. Do work (for M2.3: log processing, then set to Completed)
  6. Update user.Status = Completed, user.ProcessedAt = UtcNow
  7. Update idempotency.Status = Completed, idempotency.CompletedAt = UtcNow
  8. COMMIT transaction
```

## Implementation Plan (Commit Units)

### Unit 1 — Idempotency infrastructure: packages, entity, DbContext, migration

- **Files:**
  - `src/Worker/Kendo.Worker.csproj` — add `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore.Design`, `Polly.Core` package references
  - `src/Worker/Data/IdempotencyRecord.cs` — new entity + `IdempotencyStatus` enum
  - `src/Worker/Data/WorkerDbContext.cs` — new DbContext with idempotency table config
  - `src/Worker/Program.cs` — register `WorkerDbContext` with `UseNpgsql()`, add `AddKendoResilience()`, add `ResilientAppDbContext`-style wrapper (or reuse the shared one)
  - EF Core migration via `dotnet ef migrations add AddIdempotencyRecords`
- **Gate command:**
  ```bash
  dotnet build src/Worker/Kendo.Worker.csproj --no-restore 2>&1 && \
  dotnet ef migrations has-pending-model-changes --project src/Worker 2>&1
  ```
- **Commit message:**
  ```
  feat(worker): add IdempotencyRecords table + WorkerDbContext with EF Core

  Day 08 — unit 1 of 4 | Milestone M2.3
  ```

### Unit 2 — UserCreatedEventHandler with idempotency enforcement

- **Files:**
  - `src/Worker/Handlers/UserCreatedEventHandler.cs` — fully implemented handler
  - `src/Worker/Data/WorkerResilientDbContext.cs` — resilience wrapper for WorkerDbContext (following `ResilientAppDbContext` pattern from UserService)
- **Gate command:** `dotnet build src/Worker/Kendo.Worker.csproj --no-restore 2>&1`
- **Commit message:**
  ```
  feat(worker): implement UserCreatedEventHandler with idempotency enforcement

  Day 08 — unit 2 of 4 | Milestone M2.3
  ```

### Unit 3 — Handler tests: idempotency, crash recovery, duplicate suppression

- **Files:**
  - `tests/Kendo.Tests/Worker/UserCreatedEventHandlerTests.cs` — tests for:
    - First-time processing: user status transitions Pending → Processing → Completed
    - Duplicate message (same MessageId, Status=Completed): silent discard, no DB change
    - Crash recovery (same MessageId, Status=Processing): re-process, complete the transition
    - Unknown UserId: handler logs warning, marks idempotency as Failed, does not throw
    - IdempotencyRecord unique constraint enforcement
- **Gate command:** `dotnet test tests/Kendo.Tests/Kendo.Tests.csproj --no-restore --filter "Category=Unit" 2>&1`
- **Commit message:**
  ```
  test(worker): add UserCreatedEventHandler unit tests for idempotency + crash recovery

  Day 08 — unit 3 of 4 | Milestone M2.3
  Coverage: reported %
  Lint: clean
  ```

### Unit 4 — Integration: Docker Compose + CI verification

- **Files:**
  - `docker-compose.yml` — verify Worker connects to PostgreSQL for idempotency; no structural changes needed (Worker already uses same network)
  - `.github/workflows/ci.yml` — add `Category=Worker` or verify Worker-related tests run; ensure EF Core migration check step includes Worker project
  - `ops/runbooks/day_08_runbook.md` — create new runbook
- **Gate command:** `docker compose up -d --wait && docker compose ps --format json | grep -c "healthy" 2>&1`
- **Commit message:**
  ```
  ci: add Worker idempotency tests to CI pipeline + create runbook

  Day 08 — unit 4 of 4 | Milestone M2.3
  ```

## Success Checklist

- [ ] `UserCreatedEvent` published by `POST /api/users` is consumed by Worker's `UserCreatedEventHandler` (verified by integration test: event → handler → DB state mutation)
- [ ] First-time message processing transitions user from `Pending` → `Processing` → `Completed` with `ProcessedAt` set (verified by test)
- [ ] Duplicate message with identical `MessageId` (existing `IdempotencyRecord` with `Status=Completed`) is silently discarded — no duplicate DB writes to User (verified by test)
- [ ] Duplicate message with `IdempotencyRecord` in `Processing` state (crash recovery) re-processes and completes the transition (verified by test)
- [ ] Message with unknown `UserId` is logged and recorded as `Failed` — handler does not throw unhandled exception (verified by test)
- [ ] PostgreSQL unique constraint on `IdempotencyRecords.MessageId` enforces no duplicate records (verified by test)
- [ ] All existing tests (43/43) continue to pass after changes (regression verification)
- [ ] `docker compose up` starts all services; all `/health/ready` probes pass
- [ ] No raw Exception message returned to client on any error path — all errors flow through RFC 7807 middleware (unchanged from Day 07 — no new endpoints)

## Resilience Mandate

### Worker — `UserCreatedEventHandler`

**Database writes** (IdempotencyRecord insert + User status update):
- **Circuit Breaker:** 3 consecutive failures → 30s break (inherited from M1.3 baseline via `AddKendoResilience`)
- **Retry:** 3 attempts, exponential backoff with jitter (inherited)
- **Transaction boundary:** IdempotencyRecord insert and User status update occur within the same database transaction. If Retry exhaustion occurs mid-transaction → the outer catch logs the failure and records `IdempotencyRecord.Status = Failed`. The message is NOT committed/re-queued (Rebus default: consumer throws → message goes to DLQ after max retries). This is acceptable for M2.3 — M2.4 (DLQ consumer) will handle recovery.

**Crash recovery path:**
- If an `IdempotencyRecord` exists with `Status = Processing` (prior crash), the handler re-processes the message. This is safe because:
  - The unique constraint on `MessageId` prevents duplicate idempotency records
  - User status transitions are idempotent (Pending → Processing → Completed)
  - If the prior attempt committed the status update but not the idempotency update, re-processing sees `Processing` user and transitions to `Completed` — no harm

**No external HTTP calls:** The handler operates entirely within the database boundary. No circuit breaker needed for external services beyond the DB baseline.

**Rebus consumer configuration (unchanged):** 3 workers, 10 parallelism — already configured in `AddKendoRebus(config, "consumer")`. The `MessageId` unique constraint serializes concurrent attempts for the same message (second attempt gets unique violation, enters crash recovery path).
