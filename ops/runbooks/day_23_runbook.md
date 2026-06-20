# Day 23 Runbook — Gateway → FastAPI Client Integration

> **Milestone:** M5.4 — Gateway integration: typed client + RAG routes  
> **Date:** 2026-06-19  
> **Branch:** `feature/day-23-gateway-fastapi-routing`  

---

## Component Overview

| Component | Purpose |
|-----------|---------|
| `IFastAPIClient.RagQueryAsync` | Sync RAG query → FastAPI `POST /v1/rag/query` with Polly pipeline |
| `IFastAPIClient.RagQueryStreamAsync` | SSE streaming RAG → FastAPI `POST /v1/rag/stream` |
| `RagController.RagQuery` | `GET /api/rag/query?q=...&top_k=N` — Gateway route proxying to FastAPI |
| `RagController.RagQueryStream` | `GET /api/rag/stream?q=...&top_k=N` — SSE Gateway route |

---

## Verification

```bash
# Build
dotnet build src/Kendo.slnx

# Run FastAPI-specific unit tests
dotnet test tests/Kendo.Tests --filter "FullyQualifiedName~FastAPIIntegration"

# Start full stack with FastAPI
docker compose up -d

# Test sync RAG query through Gateway
curl -H "Authorization: Bearer <jwt>" "http://localhost:5000/api/rag/query?q=What+events+this+week&top_k=3"

# Test streaming RAG
curl -N -H "Authorization: Bearer <jwt>" "http://localhost:5000/api/rag/stream?q=Find+meetings"

# Verify auth enforcement
curl -w "\n%{http_code}" "http://localhost:5000/api/rag/query?q=test"  # 401
```

## Rollback

```bash
git revert --no-commit HEAD~3
git commit -m "revert: Day 23 M5.4 Gateway-FastAPI routing"
```
