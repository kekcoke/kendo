"""W7 Event Notification Summarization endpoint — M5.13.

POST /v1/notifications/summarize — SSE streaming endpoint for personalized
notification generation. Worker calls FastAPI directly (not through Gateway).

Uses per-tenant tone control and prompt-version tracking for audit trail.
On failure returns RFC 7807 Problem Details so Worker can DLQ rather than retry-loop.
"""

from __future__ import annotations

from typing import Any, AsyncGenerator

from fastapi import APIRouter, Depends, HTTPException, Request
from fastapi.responses import StreamingResponse

router = APIRouter(prefix="/v1/notifications", tags=["notifications"])


def _get_notification_chain(request: Request) -> Any:
    """Get or create the notification summarization chain from app state."""
    if not hasattr(request.app.state, "notification_chain"):
        from app.rag.notification_chain import NotificationSummarizationChain

        settings: Any = request.app.state.settings
        request.app.state.notification_chain = NotificationSummarizationChain(settings)
    return request.app.state.notification_chain


@router.post("/summarize")
async def summarize_notification(
    request: Request,
    body: dict[str, Any],
) -> StreamingResponse:
    """Generate a personalized event notification summary (SSE stream).

    Request body:
    ```json
    {
      "event_id": "uuid (required)",
      "user_id": "uuid (required)",
      "template_id": "string (optional)",
      "tone": "professional | friendly | urgent (default: friendly)"
    }
    ```

    Returns an SSE stream. Each chunk event contains partial notification text.
    The final "done" event includes the full notification_body, prompt_version,
    total_tokens, and trace_id.

    On error, returns RFC 7807 Problem Details so Worker can DLQ.
    """
    event_id = body.get("event_id", "").strip() if body else ""
    user_id = body.get("user_id", "").strip() if body else ""

    if not event_id:
        raise HTTPException(
            status_code=400,
            detail="'event_id' is required and cannot be empty",
        )
    if not user_id:
        raise HTTPException(
            status_code=400,
            detail="'user_id' is required and cannot be empty",
        )

    tone = body.get("tone", "friendly")

    chain = _get_notification_chain(request)

    # Build event context from request — in production this would
    # also fetch enriched event data from the database
    event_context: dict[str, Any] = {
        "event_id": event_id,
        "name": body.get("template_id", "Event"),
    }
    user_context: dict[str, Any] = {
        "user_id": user_id,
    }

    async def _event_stream() -> AsyncGenerator[bytes, None]:
        """Generate SSE-formatted stream from the chain."""
        try:
            async for chunk in chain.summarize(
                event_context=event_context,
                user_context=user_context,
                tone=tone,
            ):
                if chunk.get("_done"):
                    # Final event with metadata
                    done_data = {
                        "notification_body": chunk.get("notification_body", ""),
                        "prompt_version": chunk.get("prompt_version", ""),
                        "total_tokens": chunk.get("total_tokens", 0),
                        "trace_id": chunk.get("trace_id", ""),
                    }
                    yield f"event: done\ndata: {done_data}\n\n"
                else:
                    # Partial chunk event
                    chunk_data = {
                        "text": chunk.get("text", ""),
                        "token_count": chunk.get("token_count", 0),
                    }
                    yield f"event: chunk\ndata: {chunk_data}\n\n"
        except Exception as exc:
            # RFC 7807 error event — Worker should DLQ
            error_data = {
                "type": "about:blank",
                "title": "Summarization Failed",
                "status": 503,
                "detail": str(exc),
            }
            yield f"event: error\ndata: {error_data}\n\n"

    return StreamingResponse(
        _event_stream(),
        media_type="text/event-stream",
        headers={
            "Cache-Control": "no-cache",
            "Connection": "keep-alive",
            "X-Accel-Buffering": "no",
        },
    )
