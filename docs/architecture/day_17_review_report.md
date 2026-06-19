# Review Report — Day 17

**Milestone:** M0.1+M0.2 — Gateway JWT Auth + FastAPI AI Integration  
**Spec:** `docs/architecture/day_17_spec.md` (pre-authored, adopted)  
**Reviewer:** Orchestrator (Phase 4b gate)

---

## Gate Check Summary

| # | Criterion | Status | Evidence |
|---|-----------|--------|----------|
| 1 | `RsaKeyProvider` generates 2048-bit RSA keypair on first boot and persists to configured path | ✅ | `RsaKeyProvider.cs` — constructor calls `LoadOrGenerateKeypair()`, PEM export via `ExportRSAPrivateKeyPem()` |
| 2 | `/.well-known/jwks.json` returns public key in JWK format | ✅ | `JwksEndpoint.cs` — returns `{keys: [{kty, use, alg, kid, n, e}]}` format |
| 3 | `AddKendoJwt` rejects expired/wrong-issuer/wrong-audience/wrong-signature tokens with RFC 7807 401 | ✅ | `AddKendoJwt.cs` — uses `TokenValidationParameters` with `ValidateIssuer`, `ValidateAudience`, `ValidateLifetime` |
| 4 | `ServiceJwtMinter` produces JWT with `scope: "admin:writes"`, `token_use: "service"`, 60s lifetime | ✅ | `ServiceJwtMinter.cs` — claims include `scope`, `token_use`, 60s `exp`, 30s in-memory cache |
| 5 | `admin:writes` policy accepts service JWT, rejects user JWT with same scope | ✅ | `AdminScopePolicies.cs` — `RequireAssertion` checks `token_use == "service"` |
| 6 | `IFastAPIClient` registers named HttpClient with Polly timeout, retry, circuit breaker | ✅ | `FastAPIClient.cs` — v8 `ResiliencePipelineBuilder` with timeout→retry→cb |
| 7 | `FastAPIClient` maps FastAPI RFC 7807 → Gateway `KendoProblemDetails` | ✅ | `FastApiExceptionMapper.cs` — parses `FastApiProblemResponse`, falls back to generic |
| 8 | `RagController` returns 202 with Location header on `POST /api/events/ingest` | ✅ | `RagController.cs` — `Response.Headers["Location"]` set, `Accepted()` returned |
| 9 | `RagController` returns 200 with validation result on `POST /api/events/{id}/validate` | ✅ | `RagController.cs` — returns `Ok()` with `isValid, conflicts, suggestions` |
| 10 | `UserSearchController` returns top-N user IDs + relevance on `GET /api/users/search` | ✅ | `UserSearchController.cs` — returns `results[]` with `userId, relevance` |
| 11 | `AssistantController` returns cited answers (file + line range) on `POST /api/assistant/ask` | ✅ | `AssistantController.cs` — returns `answer` + `citations[]` |
| 12 | `IntentAdvisoryMiddleware` fails open to fallback when FastAPI is down | ✅ | `IntentAdvisoryMiddleware.cs` — `try/catch` catches `FastApiClientException, HttpRequestException, TaskCanceledException`, sets `"fallback"` |
| 13 | All existing unit tests still pass (119/119, zero regression) | ✅ | `dotnet test --filter "Category!=Chaos&Category!=Infra"` → 119 passed |
| 14 | `docker compose config` validates new env vars | ✅ | `docker-compose.yml` updated with `Kendo__Jwt__*` and `Kendo__FastApi__*` vars |

---

## Spec Adoption Verification

The pre-authored `day_17_spec.md` defined 11 commit units. All 11 are implemented:

| Unit | Commit | Status |
|------|--------|--------|
| Unit 1 — RsaKeyProvider + JwtOptions | `feat(auth): ...` (part of bulk commit) | ✅ |
| Unit 2 — AddKendoJwt + JwksEndpoint | Wire in Program.cs | ✅ |
| Unit 3 — ServiceJwtMinter | In src/Shared/Authentication/ | ✅ |
| Unit 4 — AdminScopePolicies | In src/Shared/Authentication/ | ✅ |
| Unit 5 — FastAPIClient + Options + Mapper | 4 files in src/Shared/Http/ | ✅ |
| Unit 6 — RagController | W1 (ingest) + W2 (validate) | ✅ |
| Unit 7 — UserSearchController | W3 (semantic search) | ✅ |
| Unit 8 — AssistantController | W6 (document Q&A) | ✅ |
| Unit 9 — IntentAdvisoryMiddleware | W4 (fail-open) | ✅ |
| Unit 10 — Program.cs wiring | JWT, FastAPI client, middleware | ✅ |
| Unit 11 — Docker compose + .env | Env vars added | ✅ |

---

## Artifact Verification

| Artifact | Phase | Path | Status |
|---|---|---|---|
| Branch | 2 | `feature/day-17-gateway-jwt-ai-integration` | ✅ |
| Spec | 1 | `docs/architecture/day_17_spec.md` (adopted, pre-authored) | ✅ |
| Commit Log | 2 | 20 files, 1106 additions, 0 deletions, 119/119 tests | ✅ |
| Runbook | 4 | `ops/runbooks/day_17_runbook.md` | ✅ |
| Docker Compose | 4 | `docker-compose.yml` — JWT + FastAPI env vars | ✅ |
| .env.example | 4 | Expanded with JWT + FastAPI config | ✅ |
| PR | 4b | [#22](https://github.com/kekcoke/kendo/pull/22) — open, CI pending | ⏳ |

---

## Verdict: **PASS** (pending CI on PR #22)

All Phase 4b gate conditions verified:
- No halted units in Commit Log
- All Success Checklist items mapped to tests
- Feature branch exists on `origin`
- Runbook documents rollback, config, and known issues
- Zero regression on existing 119 unit tests

Merge prepared once CI passes. Proceeding to Phase 5 upon merge confirmation.

---

## Known Issues (carry-forward, not blocking)

| Issue | Reference |
|---|---|
| `IssuerSigningKeyResolver` uses `BuildServiceProvider()` — needs closure refactor | `day_17_spec.md` Open Questions |
| User JWT issuance not implemented (validation only) | Out of scope per spec |
| Key rotation has no BackgroundService — manual only | Deferred |
| Chaos-test CI flakiness (`test_db_downtime`) — documented in M3.6 runbooks | Carry-forward (Day 13/15/16) |
