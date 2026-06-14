# Day 07 — Architecture Specification

## Milestone Scope

- **Milestone:** M2.2 — First async endpoint: at least one `POST` refactored to return `HTTP 202 Accepted` and publish a domain event
- **Roadmap phase:** 02 — Asynchronous Decoupling
- **Components touched:**
  - `UserService` — new `UsersController` with async POST endpoint, new `UserCreatedEvent` message, new polling/status endpoint
  - `Kendo.Shared` — new domain event type `UserCreatedEvent` in `Messaging/`
  - `Gateway` — (if proxying) pass through to UserService; no changes unless API gateway routing needed
- **Explicitly out of scope:**
  - M2.3 — Background consumer with idempotency key enforcement (deferred to next session)
  - M2.4 — Dead Letter Queue consumer and alerting
  - M2.5 — Transactional outbox pattern
  - M2.6 — `traceparent` propagation across producer and consumer

## Layer Changes

### Kendo.Shared (`src/Shared/Messaging/`)
New message type:
- `UserCreatedEvent.cs` — domain event published when a user is registered; extends `KendoMessage` base record

### UserService (`src/UserService/`)
- New `Models/` directory with `User.cs` entity and `UserStatus.cs` enum
- New `Controllers/UsersController.cs` — `POST /api/users` (async, 202) + `GET /api/users/{id}/status` (polling)
- New `Data/UserRepository.cs` — thin EF Core data access for user creation + status queries
- `Data/AppDbContext.cs` — add `DbSet<User>` and configure `HasPostgresExtension("vector")` (already present)
- EF Core migration: add `Users` table with status column

### Gateway (`src/Gateway/`)
- **No changes** — the Gateway currently proxies to UserService on existing routes. For M2.2, clients call UserService directly or the Gateway passes through. No Gateway changes in scope.

### Worker (`src/Worker/`)
- **No changes** — M2.2 is publish-only. The consumer (M2.3) is explicitly out of scope.

## Data Contracts

### API: `POST /api/users`

Creates a new user registration request and returns immediately with an async processing status URL.

**Request:**
```json
{
  "email": "user@example.com",
  "displayName": "Jane Doe"
}
```

**Response (202 Accepted):**
```json
{
  "userId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "status": "pending",
  "createdAt": "2026-06-13T17:30:00Z"
}
```
**Headers:**
- `Location: /api/users/a1b2c3d4-e5f6-7890-abcd-ef1234567890/status`
- `Content-Type: application/json`

**Validation (400 Bad Request — RFC 7807):**
- `email` — required, must be valid email format
- `displayName` — required, max 100 characters

**Behavior:**
1. Validate request body
2. Create `User` entity with `Status = Pending` and persist to database
3. Publish `UserCreatedEvent` via Rebus `IBus.Send()` (fire-and-forget, producer mode already wired)
4. Return `202 Accepted` with response body and `Location` header

### API: `GET /api/users/{id}/status`

Polling endpoint for checking the processing status of a user registration.

**Response (200 OK):**
```json
{
  "userId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "status": "pending",
  "createdAt": "2026-06-13T17:30:00Z",
  "processedAt": null
}
```

**Possible status values:** `pending`, `processing`, `completed`, `failed`

**Response (404 Not Found — RFC 7807):**
Returned when no user exists with the provided ID.

### Message: `UserCreatedEvent`

```csharp
namespace Kendo.Shared.Messaging;

public sealed record UserCreatedEvent : KendoMessage
{
    public Guid UserId { get; init; }
    public string Email { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
}
```

### Database: `Users` Table

| Column | Type | Constraints |
|---|---|---|
| `Id` | `uuid` | PK, default `gen_random_uuid()` |
| `Email` | `text` | NOT NULL, UNIQUE |
| `DisplayName` | `text` | NOT NULL, max 100 |
| `Status` | `text` | NOT NULL, default `'pending'`, values: `pending`, `processing`, `completed`, `failed` |
| `CreatedAt` | `timestamptz` | NOT NULL, default `now()` |
| `ProcessedAt` | `timestamptz` | NULLABLE |

### Entity: `User.cs`

```csharp
namespace Kendo.UserService.Models;

public enum UserStatus
{
    Pending,
    Processing,
    Completed,
    Failed
}

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public UserStatus Status { get; set; } = UserStatus.Pending;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; set; }
}
```

## Implementation Plan (Commit Units)

### Unit 1 — Domain event + User entity + DB migration

- **Files:**
  - `src/Shared/Messaging/UserCreatedEvent.cs` — new `UserCreatedEvent` record
  - `src/UserService/Models/User.cs` — `User` entity + `UserStatus` enum
  - `src/UserService/Data/AppDbContext.cs` — add `DbSet<User> Users` property + `OnModelCreating` configuration for users table
  - EF Core migration via `dotnet ef migrations add AddUsersTable`
- **Gate command:** `dotnet build --no-restore 2>&1 && dotnet ef migrations has-pending-model-changes --project src/UserService 2>&1`
- **Commit message:**
  ```
  feat(shared): add UserCreatedEvent domain message + User entity + migration

  Day 07 — unit 1 of 4 | Milestone M2.2
  ```

### Unit 2 — UsersController: POST /api/users (async 202) + polling GET /api/users/{id}/status

- **Files:**
  - `src/UserService/Controllers/UsersController.cs` — full controller with POST + GET
  - `src/UserService/Data/UserRepository.cs` — EF Core repository for create + get-by-id + status query
- **Gate command:** `dotnet build --no-restore 2>&1`
- **Commit message:**
  ```
  feat(userservice): add async POST /api/users returning 202 Accepted with Location header

  Day 07 — unit 2 of 4 | Milestone M2.2
  ```

### Unit 3 — Controller + repository tests

- **Files:**
  - `tests/Kendo.Tests/UserService/UsersControllerTests.cs` — tests for POST 202, POST validation errors (400), GET status (200, 404)
  - `tests/Kendo.Tests/UserService/UserRepositoryTests.cs` — in-memory EF Core tests for user creation and retrieval
- **Gate command:** `dotnet test tests/Kendo.Tests/Kendo.Tests.csproj --no-restore --filter "Category=Unit" 2>&1`
- **Commit message:**
  ```
  test(userservice): add UsersController and UserRepository unit tests

  Day 07 — unit 4 of 4 | Milestone M2.2
  Coverage: reported %
  Lint: clean
  ```

### Unit 4 — Integration: Docker Compose smoke test

- **Files:**
  - `.github/workflows/ci.yml` — add `Category=Messaging` step if not already present (verify from Day 06)
  - Docker Compose update: verify all services healthy after changes
- **Gate command:** `docker compose up -d --wait && docker compose ps --format json | grep -c "healthy" 2>&1`
- **Commit message:**
  ```
  ci: verify docker compose health after UserService changes

  Day 07 — unit 4 of 4 | Milestone M2.2
  ```

## Success Checklist

- [ ] `POST /api/users` with valid body returns `202 Accepted` and a `Location` header pointing to `/api/users/{id}/status` (verified by test)
- [ ] `POST /api/users` response body contains `userId`, `status: "pending"`, and `createdAt` (verified by test)
- [ ] `POST /api/users` publishes a `UserCreatedEvent` via Rebus `IBus.Send()` (verified by mock/bus spy)
- [ ] `POST /api/users` with missing/invalid fields returns `400 Bad Request` with RFC 7807 ProblemDetails body (verified by test)
- [ ] `GET /api/users/{id}/status` returns `200 OK` with current user status for an existing user (verified by test)
- [ ] `GET /api/users/{id}/status` returns `404 Not Found` with RFC 7807 body for a non-existent user (verified by test)
- [ ] User entity is persisted to database with `Status = Pending` on successful POST (verified by test)
- [ ] No raw Exception message returned to client on any error path — all errors flow through RFC 7807 middleware (verified by test)
- [ ] All existing tests (43/43) continue to pass after changes (regression verification)
- [ ] `docker compose up` starts all services; all `/health/ready` probes pass

## Resilience Mandate

### UserService — `POST /api/users`

**Database writes** (Create + SaveChanges):
- **Circuit Breaker:** 3 consecutive failures → 30s break (inherited from M1.3 baseline)
- **Retry:** 3 attempts, exponential backoff with jitter (inherited from M1.3 baseline)
- **Fallback:** If retries exhausted → circuit breaker returns `BrokenCircuitException` → middleware returns `503 Service Unavailable` with RFC 7807 body

**Message publish** (`IBus.Send`):
- **No circuit breaker:** Rebus internal retry handles transient transport failures. M2.5 (Outbox) is deferred but the current fire-and-forget pattern is acceptable for M2.2 as there is no transactional requirement yet.
- **No retry at call site:** Rebus handles its own retries for ASB transport. If the bus is unavailable (missing connection string), the graceful-skip path in `AddKendoRebus` means `IBus` resolves to `null` — the controller must handle this gracefully (log warning, still return 202 with pending status; the event will be published when ASB is configured).

### UserService — `GET /api/users/{id}/status`

**Database reads** (FindAsync):
- **Circuit Breaker:** 3 consecutive failures → 30s break (inherited)
- **Retry:** 3 attempts, exponential backoff with jitter (inherited)
- **Fallback:** Retry exhaustion → `503 Service Unavailable` with RFC 7807 body

### Controllers

- `UsersController` uses constructor-injected `AppDbContext` and `IBus`. The ResilientAppDbContext (scoped wrapper) is used for DB access.
- If `IBus` is null (local dev without ASB): publish step is skipped with a log warning; user is still created and 202 returned.
