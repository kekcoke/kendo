# Day 22 Runbook — LangChain RAG Pipeline + pgvector Integration

> **Milestone:** M5.2+M5.3 — LangChain RAG pipeline + read-only pgvector role  
> **Date:** 2026-06-19  
> **Branch:** `feature/day-22-rag-pipeline` → merged via PR #28  

---

## Component Overview

| Component | Purpose |
|-----------|---------|
| `app/db.py` | asyncpg connection pool using `fastapi_ro` role (SELECT-only) |
| `app/repositories/embeddings.py` | pgvector cosine similarity search (`embedding <=> $1`) |
| `app/rag/chain.py` | `KendoRAGChain` — embed query, retrieve contexts, build answer |
| `app/rag/prompts.py` | Versioned prompt templates (v1) |
| `app/rag/embeddings_client.py` | BGE-large-en-v1.5 local embedding client (CPU) |
| `app/api/v1/rag.py` | `POST /v1/rag/query` (sync) + `POST /v1/rag/stream` (SSE) |
| `app/middleware/auth.py` | Upgraded to real RS256 JWKS validation via `CachedJWKSClient` |
| `app/middleware/jwks.py` | JWKS client with 1-hour TTL, refresh-on-miss |
| `app/routes/health.py` | Updated `/health/ready` to check pgvector connectivity |

---

## Dependencies

| Dependency | Version | Purpose |
|---|---|---|
| `asyncpg` | ≥0.31.0 | Async PostgreSQL connection pool |
| `httpx` | ≥0.28.0 | JWKS fetching from Gateway |
| `pyjwt` + `cryptography` | ≥2.10 / ≥44.0 | RS256 JWT validation |
| `langchain` + `langchain-community` | ≥0.4 / ≥1.3 | RAG chain framework |
| `sentence-transformers` | ≥3.4.0 | BGE embedding model (local dev) |
| `pytest` | ≥9.0.0 | Test suite |

---

## Verification

```bash
# Start all services
docker compose up -d

# Health endpoints
curl -f http://fastapi:8000/health/live
curl -f http://fastapi:8000/health/ready  # 200 if pgvector reachable, 503 otherwise

# RAG query (requires valid Gateway-issued JWT)
curl -X POST http://fastapi:8000/v1/rag/query \
  -H "Authorization: Bearer <jwt>" \
  -H "Content-Type: application/json" \
  -d '{"query": "What events happened last week?", "top_k": 5}'

# Streaming RAG
curl -X POST http://fastapi:8000/v1/rag/stream \
  -H "Authorization: Bearer <jwt>" \
  -H "Content-Type: application/json" \
  -d '{"query": "Find events about team meetings"}'

# Auth enforcement
curl -w "\n%{http_code}" http://fastapi:8000/v1/rag/query  # 401
```

## Rollback

```bash
git revert --no-commit 73f744f
git commit -m "revert: Day 22 M5.2+M5.3 FastAPI RAG pipeline"
```
