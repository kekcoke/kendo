"""User Intent Classification endpoint — W4 (M5.10).

POST /v1/intent/classify — accepts raw request body + user_id + available
routes, returns { route, confidence } within 80ms p95.

FastAPI is advisory-only. Gateway always has a hard-coded fallback route,
so a FastAPI outage degrades to previous behavior — not a 500.

Design constraints:
- p95 latency ≤ 80ms (classification is on the request hot path)
- Confidence score >= 0.0 <= 1.0
- Tenacity retry: 2 retries, 100ms backoff before returning fallback
- Health endpoints exempt (handled by middleware)
"""

from __future__ import annotations

from typing import Any

from fastapi import APIRouter, HTTPException, Request

from app.rag.intent_classifier import IntentClassifier

router = APIRouter(prefix="/v1/intent", tags=["intent"])


def _get_classifier(request: Request) -> IntentClassifier:
    """Get or create the intent classifier from app state."""
    if not hasattr(request.app.state, "intent_classifier"):
        settings: Any = request.app.state.settings
        request.app.state.intent_classifier = IntentClassifier(settings)
    return request.app.state.intent_classifier


@router.post("/classify")
async def classify_intent(
    request: Request,
    body: dict[str, Any],
) -> dict[str, Any]:
    """Classify the intent of a raw request body (W4).

    Request body:
    ```json
    {
      "body": "raw request body text (required)",
      "user_id": "uuid from JWT sub (optional)",
      "available_routes": ["events.ingest", "events.validate", ...]
    }
    ```

    Returns:
    ```json
    {
      "route": "events.ingest",
      "confidence": 0.87
    }
    ```

    FastAPI is advisory-only — the Gateway holds the route table.
    """
    raw_body = body.get("body", "").strip() if body else ""
    if not raw_body:
        raise HTTPException(
            status_code=400,
            detail="'body' field is required and cannot be empty",
        )

    user_id = body.get("user_id")
    available_routes = body.get("available_routes", [])

    if not available_routes:
        raise HTTPException(
            status_code=400,
            detail="'available_routes' field is required and must be a non-empty list",
        )

    classifier = _get_classifier(request)
    result = await classifier.classify(
        body=raw_body,
        user_id=user_id,
        available_routes=available_routes,
    )

    return result
