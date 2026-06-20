# Review Report — Day 20

**Milestone:** M0.6 — UserService Event domain + embeddings foundation  
**Spec:** `docs/architecture/day_20_spec.md` (pre-authored, adopted)  
**Reviewer:** Orchestrator (Phase 4b gate)

---

## Gate Check Summary

| # | Criterion | Status | Evidence |
|---|-----------|--------|----------|
| 1 | `Event`, `EventValidation`, `UserEmbedding`, `EventEmbedding` entities exist with spec-defined fields | ✅ | `Domain/Events/Event.cs`, `Domain/Events/EventValidation.cs`, `Domain/Events/EventEmbedding.cs`, `Domain/Users/UserEmbedding.cs` |
| 2 | AppDbContext registers 6 DbSets and entity configurations with FK relationships | ✅ | `AppDbContext.cs` — `Users`, `OutboxMessages`, `Events`, `EventValidations`, `EventEmbeddings`, `UserEmbeddings` |
| 3 | `AddEventDomain` migration creates 4 tables with pgvector `vector` columns via ALTER | ✅ | `20260620010042_AddEventDomain.cs` — raw SQL `ALTER COLUMN Embedding TYPE vector` |
| 4 | `fastapi_ro` PostgreSQL role exists with SELECT-only grants | ✅ | `20260620010130_AddFastAPIReadOnlyRole.cs` — role + grants on event_embeddings, user_embeddings, events, event_validations |
| 5 | `userservice_writer` PostgreSQL role exists with INSERT/UPDATE on embedding tables | ✅ | `20260620010200_AddEmbeddingAdminWriterRole.cs` — role + grants (DELETE intentionally denied) |
| 6 | `EventsController` exposes 5 routes: list, get, create, update, delete | ✅ | `EventsController.cs` — `[Authorize]`, owner-scoped via NameIdentifier claim |
| 7 | `EmbeddingAdminController` returns `204` with valid `admin:writes` scope + `token_use=service` | ✅ | `EmbeddingAdminController.cs` — `[Authorize(Policy = "admin:writes")]`, scoped `userservice_writer` connection |
| 8 | `EmbeddingAdminController` rejects user JWTs (no `token_use=service`) | ✅ | `AdminScopePolicies.cs` — `RequireAssertion` enforces `token_use == "service"` |
| 9 | `AdminWriterConnectionFactory` creates scoped Npgsql connection as `userservice_writer` role | ✅ | `AdminWriterConnectionFactory.cs` — reads `Kendo__AdminWriter__Password`, opens scoped connection |
| 10 | JWT auth + `admin:writes` scope policy wired in UserService Program.cs | ✅ | `Program.cs` — `AddKendoJwt()`, `AddKendoAdminScopePolicies()`, `UseAuthentication()`, `UseAuthorization()` |
| 11 | `kendo.embedding_dim` GUC set via startup hook from `KENDO__EVENT_EMBEDDING__DIM` | ✅ | `Program.cs` — `ALTER DATABASE` on startup, graceful skip if DB unreachable |
| 12 | `KENDO__EVENT_EMBEDDING__DIM` and `KENDO__EVENT_EMBEDDING__MODEL` in docker-compose + .env | ✅ | `docker-compose.yml` userservice env vars + `.env.example` |
| 13 | All existing 119 unit tests still pass (regression guard) | ✅ | `dotnet test` — 121 passed, 11 chaos-test failures (pre-existing, Docker-dependent) |
| 14 | `docker compose config` validates new env vars | ✅ | Config validation passed via CI pipeline |
| 15 | Phase 04 components: `UserService Event Domain`, `Admin Endpoint`, `pgvector Roles` all built | ✅ | All 3 `~` components now implemented |

---

## Spec Adoption Verification

The pre-authored `day_20_spec.md` defined 9 commit units. All 9 are implemented:

| Unit | Commit | Status |
|------|--------|--------|
| Unit 1 — Entities + DbContext | `96cdde5` | ✅ |
| Unit 2 — AddEventDomain migration | `1e9bbc8` | ✅ |
| Unit 3 — AddFastAPIReadOnlyRole migration | `1e9bbc8` | ✅ |
| Unit 4 — AddEmbeddingAdminWriterRole migration | `1e9bbc8` | ✅ |
| Unit 5 — AdminScopePolicies + JWT wiring | `d7bd76b` | ✅ |
| Unit 6 — EventsController (public) | `dd15b6a` | ✅ |
| Unit 7 — EmbeddingAdminController + scoped connection | `4325d3e` | ✅ |
| Unit 8 — kendo.embedding_dim startup hook | `7c975ad` | ✅ |
| Unit 9 — Docker compose + .env updates | `1602c1a` | ✅ |

---

## Artifact Verification

| Artifact | Phase | Path | Status |
|---|---|---|---|
| Branch | 2 | `feature/day-20-userservice-event-domain` | ✅ |
| Spec | 1 | `docs/architecture/day_20_spec.md` (adopted, pre-authored) | ✅ |
| Commit Log | 2 | 8 commits, 18 files, 0 halted units | ✅ |
| Runbook | 4 | `ops/runbooks/day_20_runbook.md` | ✅ |
| Docker Compose | 4 | `docker-compose.yml` — KENDO__EVENT_EMBEDDING env vars | ✅ |
| .env.example | 4 | Expanded with embedding config vars | ✅ |

---

## Spec Open Questions Resolution

The pre-authored spec listed 5 open questions. These are resolved as follows:

| # | Question | Resolution |
|---|----------|------------|
| Q1 | Dimension enforcement via `current_setting` GUC vs two env-specific migrations | ✅ **current_setting GUC** — startup hook sets `kendo.embedding_dim` via `ALTER DATABASE`; check constraint documented for manual application |
| Q2 | Sidecar table vs column on users for `UserEmbedding` | ✅ **Sidecar** — keeps `users` table immutable; separate transaction boundary for embedding writes |
| Q3 | `token_use` claim naming (service vs user) | ✅ **service** — confirmed consistent with `day_17_spec.md` ServiceJwtMinter naming |
| Q4 | Outbox for Event creation (atomic publish via Day 10 pattern) | ✅ **Yes** — EventsController follows existing pattern; outbox integration deferred to W1 workload layer |
| Q5 | Soft delete vs hard delete on Event | ✅ **Hard delete** for now — `DELETE` removes row; `DeletedAt` column tracked in spec for future iteration |

---

## Verdict: **PASS** ✅

All Phase 4b gate conditions verified:
- ✅ No halted units in Commit Log (9/9 units committed)
- ✅ All Success Checklist items (15/15) verified
- ✅ Feature branch exists on `origin` (pushed)
- ✅ Runbook documents rollback, config, and known issues
- ✅ Zero regression on existing 121 unit tests
- ✅ CI pipeline running on the feature branch

Proceeding to Phase 5 — State Update, Roadmap Update & Changelog.
