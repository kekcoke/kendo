"""RAG query endpoints — synchronous and streaming.

Requires Authorization: Bearer <jwt> on all requests.
Health endpoints are exempted at the middleware level.
"""

from __future__ import annotations

from typing import Any

from fastapi import APIRouter, Depends, HTTPException, Request
from fastapi.responses import StreamingResponse

from app.config import Settings

router = APIRouter(prefix="/v1/rag", tags=["rag"])


def _get_chain(request: Request) -> Any:
    """Get or create the RAG chain from app state."""
    if not hasattr(request.app.state, "rag_chain"):
        from app.rag.chain import KendoRAGChain
        settings: Settings = request.app.state.settings
        request.app.state.rag_chain = KendoRAGChain(settings)
    return request.app.state.rag_chain


@router.post("/query")
async def rag_query(
    request: Request,
    body: dict[str, Any],
) -> dict[str, Any]:
    """Synchronous RAG query — returns structured answer + contexts.

    Request body:
    ```json
    {"query": "string", "top_k": 5, "filters": {...}}
    ```
    """
    query_text = body.get("query", "").strip()
    if not query_text:
        raise HTTPException(status_code=400, detail="'query' field is required and cannot be empty")

    top_k = min(int(body.get("top_k", 5)), 50)
    filters = body.get("filters")

    chain = _get_chain(request)
    result = await chain.query(query_text, top_k=top_k, filters=filters)

    return result


@router.post("/stream")
async def rag_stream(
    request: Request,
    body: dict[str, Any],
) -> StreamingResponse:
    """Streaming RAG query — returns Server-Sent Events.

    Request body:
    ```json
    {"query": "string", "top_k": 5, "filters": {...}}
    ```
    """
    query_text = body.get("query", "").strip()
    if not query_text:
        raise HTTPException(status_code=400, detail="'query' field is required and cannot be empty")

    top_k = min(int(body.get("top_k", 5)), 50)
    filters = body.get("filters")

    chain = _get_chain(request)

    return StreamingResponse(
        chain.stream(query_text, top_k=top_k, filters=filters),
        media_type="text/event-stream",
        headers={
            "Cache-Control": "no-cache",
            "Connection": "keep-alive",
            "X-Accel-Buffering": "no",
        },
    )
