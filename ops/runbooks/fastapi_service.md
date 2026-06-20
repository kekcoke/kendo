# FastAPI Service — Incident Runbooks

> Consolidated failure-mode playbook for the Python AI/Vector service.  
> All other services retain their per-day runbooks in `ops/runbooks/`.  
> Last updated: 2026-06-19 (Day 25 — M5.6)

---

## Table of Contents

1. [Azure OpenAI Outage](#azure-openai-outage)
2. [pgvector Read Replica Failover](#pgvector-read-replica-failover)
3. [FastAPI Crash Loop](#fastapi-crash-loop)
4. [JWKS Rotation](#jwks-rotation)
5. [Known CI Flakiness](#known-ci-flakiness)

---

## Azure OpenAI Outage

### Symptoms
- FastAPI `/health/ready` shows `llm: unhealthy`
- RAG queries return 503 with `"detail": "LLM service unavailable"`
- Circuit breaker (`openai_breaker`) transitions to OPEN state
- Gateway sees Polly circuit breaker trips for FastAPI proxy (secondary symptom)

### Detection
- `/health/ready` endpoint checks LLM connectivity via a lightweight ping call
- pybreaker `CircuitBreakerListener` logs state transitions
- OpenTelemetry span events record each breaker state change

### Response

1. **Verify the outage is not a transient blip:**
   ```bash
   curl -fsS http://localhost:8000/health/ready
   # Look for "llm": "unhealthy" in the response
   curl -fsS http://localhost:8000/health/ready 2>/dev/null | python -m json.tool
   ```

2. **Check Azure OpenAI dashboard** for region health, throttling, or quota exhaustion.

3. **If transient** (≤ 30s outage):
   - Wait for pybreaker `reset_timeout` (default 30s) to transition to HALF-OPEN
   - Next successful request will close the breaker automatically

4. **If prolonged** (> 5 minutes):
   - Consider switching to fallback LLM provider (requires config change):
     ```bash
     FASTAPI__LLM__ENDPOINT=https://fallback-openai.openai.azure.com
     FASTAPI__LLM__API_KEY=<fallback-key>
     docker compose up -d fastapi
     ```
   - Update environment with new endpoint and restart FastAPI

5. **If provider-wide outage:**
   - Degrade RAG to retrieval-only mode (no LLM generation)
   - Set `FASTAPI__LLM__ENABLED=false` and restart
   - RAG queries will return retrieved context chunks without synthesis

### Recovery Verification
```bash
curl -fsS http://localhost:8000/health/ready | python -c "import sys, json; d=json.load(sys.stdin); assert d.get('llm') == 'healthy'"
```

### Post-Mortem
- Log the outage start/end timestamps with trace IDs
- File a ticket with Azure Support referencing affected deployment

---

## pgvector Read Replica Failover

### Symptoms
- FastAPI `/health/ready` shows `pgvector: unhealthy`
- RAG queries return 503 with `"detail": "Vector database unavailable"`
- pgvector circuit breaker transitions to OPEN state
- UserService (which writes to pgvector) may also be affected

### Detection
- `/health/ready` endpoint checks pgvector connectivity via a test query
- pybreaker pgvector breaker state logged to OpenTelemetry

### Response

1. **Verify pgvector cluster status:**
   ```bash
   docker compose exec postgres pg_isready -U kendo -d kendo_users
   ```

2. **If read replica is down (not primary):**
   - Update `FASTAPI__VECTOR__READ_DSN` to point to the healthy replica
   - Restart FastAPI:
     ```bash
     docker compose up -d fastapi
     ```

3. **If primary is also affected:**
   - FastAPI will return 503 for all RAG queries
   - Gateway will trip its Polly circuit breaker to FastAPI
   - This is acceptable — do NOT bypass read-only enforcement
   - Focus on restoring PostgreSQL primary first (see `ops/runbooks/db-failover.md`)

4. **After recovery:**
   - Wait for pybreaker `reset_timeout` (default 30s) to transition to HALF-OPEN
   - Verify with health endpoint

### Recovery Verification
```bash
curl -fsS http://localhost:8000/health/ready | python -m json.tool
# pgvector must show "healthy"
```

### Known CI Flakiness (inherited from M3.6)
The `test_db_downtime` chaos test intermittently receives `000000` (connection refused) instead of the expected `503` response. This occurs when docker compose unpause completes faster than the application process pool reconnects.

**Mitigation:** The chaos test fixture includes a retry loop after `docker compose unpause`:
```python
# Pseudocode for integration test:
docker_compose_unpause("postgres")
for _ in range(5):
    resp = await client.get("/health/ready")
    if resp.status_code == 200:
        break
    await asyncio.sleep(2)
```

---

## FastAPI Crash Loop

### Symptoms
- Gateway logs show `Connection refused` or `503` for `/api/rag/*` routes
- `docker compose ps` shows FastAPI container in restart loop
- FastAPI logs show repeated startup failure

### Detection
- Docker health check logs: `docker compose logs fastapi`
- Gateway Polly circuit breaker logs show repeated failures

### Response

1. **Check container status and logs:**
   ```bash
   docker compose ps fastapi
   docker compose logs fastapi --tail 50
   ```

2. **Common causes:**
   - **pgvector unreachable at startup:** FastAPI requires pgvector to be healthy before `/health/ready` passes. Check PostgreSQL connectivity.
   - **JWT configuration error:** Wrong JWKS URL or audience. Verify `FASTAPI__JWT__*` env vars.
   - **Dependency version mismatch:** Check `pyproject.toml` dependencies for breaking changes.
   - **Azure OpenAI config invalid:** Wrong endpoint, API key, or deployment name.

3. **Recovery per cause:**

   **pgvector unreachable:**
   ```bash
   # Verify PostgreSQL is healthy
   docker compose exec postgres pg_isready -U kendo
   # Fix connection string if needed
   docker compose up -d fastapi
   ```

   **JWT misconfiguration:**
   ```bash
   # Test JWKS endpoint
   curl -fsS http://gateway:5000/.well-known/jwks.json
   # Verify env vars match
   docker compose exec fastapi env | grep FASTAPI__JWT
   ```

   **Dependency issue:**
   ```bash
   # Rebuild with --no-cache to ensure fresh deps
   docker compose build --no-cache fastapi
   docker compose up -d fastapi
   ```

4. **If crash loop persists > 5 attempts:**
   - Investigate with `--tail 200` and trace correlation
   - Consider scaling down: `docker compose scale fastapi=0` to stop the loop
   - Root cause analysis before re-deploying

### Recovery Verification
```bash
docker compose up -d fastapi
sleep 10
curl -fsS http://localhost:8000/health/live && curl -fsS http://localhost:8000/health/ready
```

---

## JWKS Rotation

### Symptoms
- JWT validation failures: 401 with `"detail": "Signing key for kid='...' not found in JWKS"`
- Token issuance works before rotation but fails after

### Detection
- FastAPI logs show `KeyNotFoundException` or `JWKS fetch failed`
- OpenTelemetry spans show `auth.jwks_miss` attribute

### Response

1. **Check Gateway JWKS endpoint:**
   ```bash
   curl -fsS http://gateway:5000/.well-known/jwks.json | python -m json.tool
   ```

2. **Trigger JWKS cache refresh:**
   The `CachedJWKSClient` caches JWKS for 1 hour by default. To force refresh:
   ```bash
   # Restart FastAPI to clear in-memory cache
   docker compose restart fastapi
   ```

3. **If Gateway itself has stale keys:**
   - Gateway JWKS is served in-memory. Check `KENDO__JWT__PRIVATE_KEY_PATH`.
   - If private key was rotated, Gateway must be restarted to reload.

4. **If rotation failed and tokens cannot be verified:**
   - Rollback the private key to the previous rotation
   - Clear JWKS cache by restarting FastAPI
   - Re-attempt rotation after verifying compatibility

### Recovery Verification
```bash
# Issue a test token via Gateway
# Then verify FastAPI accepts it
curl -fsS -X POST http://localhost:8000/v1/rag/query \
  -H "Authorization: Bearer <test-token>" \
  -H "Content-Type: application/json" \
  -d '{"query": "test"}'
```

---

## Known CI Flakiness

### test_db_downtime — intermittent `000000` connection refused

**Symptom:** The chaos test `test_db_downtime` returns `000000` (connection refused) instead of expected `503`.

**Suspected root cause:** Docker compose unpause completes faster than the application process re-establishes its asyncpg connection pool. The test fixture asserts on the first response, which arrives before the app has reconnected.

**Mitigation approaches (from M3.6):**
1. ✅ **Adopted (Day 13):** Retry loop in test fixture — wait for `/health/ready` to return 200 after unpause before asserting.
2. ⏳ **Recommended (future):** Add a circuit-breaker state metric gauge (`circuit_breaker_state`) that the test can poll instead of retrying with a sleep.
3. ❌ **Not pursued:** Removing the chaos test entirely (loss of signal is worse than flakiness).

**Current state:** ~90% pass rate in CI. Not blocking pipeline (chaos tests run after main build-and-test).
