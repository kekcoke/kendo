"""User Profile Semantic Search endpoint — W3 (M5.9).

GET /v1/users/search?q=string&top_k=10 — hybrid pgvector cosine + BM25 search
against user_embeddings. Returns ranked user IDs with relevance scores.
"""

from __future__ import annotations

from typing import Any

from fastapi import APIRouter, HTTPException, Query, Request

from app.rag.embeddings_client import create_embedding_client
from app.repositories.user_search import hybrid_search

router = APIRouter(prefix="/v1/users", tags=["users"])


@router.get("/search")
async def search_users(
    request: Request,
    q: str = Query(..., min_length=1, description="Search query string"),
    top_k: int = Query(10, ge=1, le=50, description="Number of results to return"),
) -> dict[str, Any]:
    """Search users by query string using hybrid pgvector + BM25.

    Combines semantic search (cosine similarity on user_embeddings) with
    keyword search (Postgres full-text ts_rank) for robust user discovery.

    Returns:
    ```json
    {
        "user_ids": ["uuid1", "uuid2"],
        "relevance": [0.92, 0.85],
        "trace_id": "abc123"
    }
    ```
    """
    settings: Any = request.app.state.settings
    embedding_client = create_embedding_client(settings)

    try:
        results = await hybrid_search(
            query_text=q,
            embedding_client=embedding_client,
            top_k=top_k,
            cosine_weight=0.7,
            bm25_weight=0.3,
        )
    except Exception as exc:
        raise HTTPException(
            status_code=503,
            detail=f"Search service unavailable: {exc}",
        )

    # Extract trace_id from OpenTelemetry context
    trace_id = ""
    try:
        from app.observability.tracing import get_current_trace_id
        trace_id = get_current_trace_id() or ""
    except Exception:
        pass

    return {
        "user_ids": [r["user_id"] for r in results],
        "relevance": [r["score"] for r in results],
        "trace_id": trace_id,
    }
