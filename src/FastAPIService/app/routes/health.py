"""Health check endpoints — liveness and readiness probes."""

from __future__ import annotations

from fastapi import APIRouter, Request, Response
from fastapi.responses import JSONResponse

router = APIRouter(tags=["health"])


@router.get("/health/live")
async def health_live() -> dict[str, str]:
    """Liveness probe — always returns 200 when the process is responsive."""
    return {"status": "healthy"}


@router.get("/health/ready")
async def health_ready(request: Request) -> Response:
    """Readiness probe — returns 200 when all dependencies are reachable.

    Checks: pgvector reachable (if DSN configured).
    Returns 503 RFC 7807 with failing dependency named.
    """
    settings = request.app.state.settings

    # Check pgvector if configured
    if settings.vector_read_dsn:
        from app.db import check_connection

        if not await check_connection():
            return JSONResponse(
                status_code=503,
                content={
                    "type": "about:blank",
                    "title": "Service Unavailable",
                    "status": 503,
                    "detail": "pgvector not reachable",
                    "trace_id": "",
                },
                headers={"Content-Type": "application/problem+json"},
            )

    return JSONResponse(content={"status": "ready"})
