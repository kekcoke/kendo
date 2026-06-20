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

    At M5.1 scaffold stage, always returns ready.
    Will be wired to pgvector + Azure OpenAI checks in M5.3+.
    """
    return JSONResponse(content={"status": "ready"})
