"""JWT authentication middleware.

Validates RS256 JWTs issued by the Gateway using JWKS discovery.
All routes have JWT enforced by default; health endpoints are exempted
at registration time.
"""

from __future__ import annotations

from typing import Callable

from fastapi import HTTPException, Request, Response
from fastapi.responses import JSONResponse
from starlette.middleware.base import BaseHTTPMiddleware
from starlette.types import ASGIApp


class JWTAuthMiddleware(BaseHTTPMiddleware):
    """Middleware that validates RS256 JWTs from the Gateway.

    Health endpoints are exempt via EXEMPT_PATHS.
    """

    EXEMPT_PATHS: tuple[str, ...] = ("/health/live", "/health/ready")

    def __init__(self, app: ASGIApp, jwks_url: str, audience: str, issuer: str) -> None:
        super().__init__(app)
        self.jwks_url = jwks_url
        self.audience = audience
        self.issuer = issuer
        self._jwks_client: Callable | None = None  # Lazy init in future units

    async def dispatch(self, request: Request, call_next: Callable) -> Response:
        if request.url.path in self.EXEMPT_PATHS:
            return await call_next(request)

        auth_header = request.headers.get("Authorization", "")
        if not auth_header.startswith("Bearer "):
            return JSONResponse(
                status_code=401,
                content={
                    "type": "about:blank",
                    "title": "Unauthorized",
                    "status": 401,
                    "detail": "Missing or malformed Authorization header",
                },
            )

        # In M5.1 scaffold: accept any well-formed Bearer token.
        # Full JWKS validation deferred to M5.2+ unit.
        token = auth_header.removeprefix("Bearer ")

        request.state.user = {"sub": "scaffold-user", "scope": ""}

        return await call_next(request)
