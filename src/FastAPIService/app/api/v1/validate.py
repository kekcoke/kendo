"""Event Validation endpoint — W2 (M5.8).

POST /v1/rag/validate — accepts structured Event JSON + user_id,
runs multi-step reasoning to detect scheduling conflicts, headcount
plausibility, and time collisions. Returns ValidationResult with
reasoning trace.
"""

from __future__ import annotations

from typing import Any

from fastapi import APIRouter, Depends, HTTPException, Request

router = APIRouter(prefix="/v1/rag", tags=["rag"])


def _get_validate_chain(request: Request) -> Any:
    """Get or create the validate chain from app state."""
    if not hasattr(request.app.state, "validate_chain"):
        from app.rag.validate_chain import ValidateChain
        settings: Any = request.app.state.settings
        request.app.state.validate_chain = ValidateChain(settings)
    return request.app.state.validate_chain


@router.post("/validate")
async def validate_event(
    request: Request,
    body: dict[str, Any],
) -> dict[str, Any]:
    """Validate a candidate Event for conflicts (W2).

    Request body:
    ```json
    {
      "event": { /* structured Event JSON from W1 */ },
      "user_id": "string (required)"
    }
    ```

    Returns ValidationResult with ok, conflicts, suggestions,
    and reasoning_trace.
    """
    event = body.get("event") if body else None
    if not event:
        raise HTTPException(
            status_code=400,
            detail="'event' field is required and cannot be empty",
        )

    user_id = body.get("user_id")
    if not user_id:
        raise HTTPException(
            status_code=400,
            detail="'user_id' field is required",
        )

    chain = _get_validate_chain(request)
    result = await chain.validate(event, user_id)

    return result
