"""Event Ingestion RAG endpoint — W1 (M5.7).

POST /v1/rag/ingest — accepts free-form event text, returns structured Event JSON.
Follows the same pattern as rag.py (auth, health exemption, RFC 7807).
"""

from __future__ import annotations

from typing import Any

from fastapi import APIRouter, Depends, HTTPException, Request

router = APIRouter(prefix="/v1/rag", tags=["rag"])


def _get_ingest_chain(request: Request) -> Any:
    """Get or create the ingest chain from app state."""
    if not hasattr(request.app.state, "ingest_chain"):
        from app.rag.ingest_chain import IngestChain
        settings: Any = request.app.state.settings
        request.app.state.ingest_chain = IngestChain(settings)
    return request.app.state.ingest_chain


@router.post("/ingest")
async def ingest_event(
    request: Request,
    body: dict[str, Any],
) -> dict[str, Any]:
    """Ingest free-form event text → structured Event JSON (W1).

    Request body:
    ```json
    {"text": "string (required)", "user_id": "string (optional)"}
    ```

    Returns structured Event JSON with fields extracted by the LLM
    using context from similar past events in pgvector.
    """
    text = body.get("text", "").strip() if body else ""
    if not text:
        raise HTTPException(
            status_code=400,
            detail="'text' field is required and cannot be empty",
        )

    user_id = body.get("user_id")

    chain = _get_ingest_chain(request)
    result = await chain.ingest(text, user_id=user_id)

    return result
