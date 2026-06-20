# Day 20 Runbook — UserService Event Domain + Embeddings

> **Milestone:** M0.6 — UserService Event domain + embeddings foundation  
> **Date:** 2026-06-20  
> **Branch:** `feature/day-20-userservice-event-domain`  

---

## Component Overview

| Component | Purpose |
|-----------|---------|
| `Event` entity | New domain root for Event Scheduling workflow; stores structured event data from W1 ingestion |
| `EventValidation` entity | 1:1 with Event; stores W2 conflict validation results (`ConflictsJson`, `SuggestionsJson`, `ReasoningTrace`) |
| `EventEmbedding` entity | Sidecar table for event vector embeddings; pgvector `vector` column type |
| `UserEmbedding` entity | Sidecar table for user vector embeddings; enables W3 semantic search |
| `EventsController` | Public API — 5 JWT-authenticated routes (list, get, create, update, delete) |
| `EmbeddingAdminController` | Internal API — admin:writes scope; scoped `userservice_writer` connection |
| `AdminWriterConnectionFactory` | Scoped Npgsql connection factory as `userservice_writer` role |
| `AddFastAPIReadOnlyRole` | PostgreSQL `fastapi_ro` role — SELECT-only grants for FastAPI Phase 05 |
| `AddEmbeddingAdminWriterRole` | PostgreSQL `userservice_writer` role — INSERT/UPDATE grants only |
| `AddEventDomain` migration | Composite migration — events, event_validations, event_embeddings, user_embeddings tables |

---

## Configuration

### Required Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `KENDO__EVENT_EMBEDDING__DIM` | 1024 | pgvector dimension for embeddings (1024 local, 1536 cloud) |
| `KENDO__EVENT_EMBEDDING__MODEL` | BGE-large-en-v1.5 | Embedding model name |
| `KENDO__ADMIN_WRITER__PASSWORD` | changeme_userservice_writer | Password for the `userservice_writer` PostgreSQL role |
| `KENDO__FASTAPI__RO__PASSWORD` | changeme_fastapi_ro | Password for the `fastapi_ro` PostgreSQL role |

### Graceful Skip

- `AddKendoJwt` — skips if `Kendo:Jwt` config section is absent (UserService starts, but JWT-authenticated endpoints will return 401)
- `AddKendoAdminScopePolicies` — safe no-op if no authorization is configured
- `KENDO__EVENT_EMBEDDING__DIM` start-up GUC set — graceful skip if DB unreachable

---

## Deployment Steps

### Prerequisite
- [ ] PostgreSQL instance running with pgvector extension (already ✅ from Day 02)
- [ ] `KENDO__ADMIN_WRITER__PASSWORD` configured (default works for local dev)
- [ ] `KENDO__FASTAPI__RO__PASSWORD` configured (default works for local dev)

### Deployment
1. Deploy `src/UserService/` — new entities, controllers, migrations, and DbContext
2. Run Migrations:
   ```bash
   dotnet ef database update --project src/UserService
   ```
3. The startup hook will automatically set `kendo.embedding_dim` GUC on next restart
4. Verify with:
   ```bash
   docker compose build userservice
   docker compose up -d userservice
   docker compose logs userservice | grep -i "embedding_dim\|embedding"
   ```

### Verification
```bash
# Build and start
docker compose build userservice
docker compose up -d userservice

# Check health
curl -f http://localhost:5001/health/live
curl -f http://localhost:5001/health/ready

# Verify EventsController
curl -s -H "Authorization: Bearer $USER_JWT" http://localhost:5001/api/events | jq .

# Verify EmbeddingAdminController (service JWT only)
curl -s -X POST http://localhost:5001/internal/embeddings \
  -H "Authorization: Bearer $SERVICE_JWT" \
  -H "Content-Type: application/json" \
  -d '{"target":"event","target_id":"00000000-0000-0000-0000-000000000001","model_name":"test","dimensions":4,"embedding":[0.1,0.2,0.3,0.4]}'
```

---

## Rollback

### If migrations need reverting:
```bash
# Roll back all 3 migrations in order
dotnet ef migrations remove --project src/UserService  # AddEmbeddingAdminWriterRole
dotnet ef migrations remove --project src/UserService  # AddFastAPIReadOnlyRole
dotnet ef migrations remove --project src/UserService  # AddEventDomain
```

### If EmbeddingAdminController is misconfigured:
1. Remove `EmbeddingAdminController.cs` and `AdminWriterConnectionFactory.cs`
2. Remove `AddKendoAdminScopePolicies()` and `AddKendoJwt()` from `Program.cs`
3. Remove the `userservice_writer` and `fastapi_ro` role migrations
4. Rebuild and redeploy

---

## Known Issues

- **UserService with JWT validation requires Gateway:** JWT configuration (`Kendo:Jwt`) must be present for authenticated endpoints. Until the Gateway is deployed with JWKS, UserService endpoints will require manual config.
- **EmbeddingAdminController requires service JWT:** The `token_use=service` claim is only present in service JWTs minted by the Gateway's `ServiceJwtMinter`. Regular user JWTs cannot write embeddings.
- **FastAPI not yet deployed:** Embedding operations through `EmbeddingAdminController` will work, but actual AI workload integration (W1-W7) requires Phase 05 FastAPI deployment.
- **Dimension check constraint is best-effort:** The `kendo.embedding_dim` GUC is set via `ALTER DATABASE` on startup. If the database is unreachable at startup, the GUC won't be applied until the next restart. The check constraint itself (`event_embeddings_dim_check`) must be created separately — it's documented in the spec but not auto-applied by migrations.

---

## CI Integration

The existing CI pipeline (`build-and-test`) covers:
1. ✅ All 121+ unit tests (including new Entity/DbContext tests)
2. ✅ Compilation verification for all projects
3. ✅ Docker compose config validation
4. ✅ Data integration tests with pgvector service container

No infrastructure changes required — all changes are data-layer only.
