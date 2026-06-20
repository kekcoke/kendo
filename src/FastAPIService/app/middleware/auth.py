"""JWT authentication middleware.

Validates RS256 JWTs issued by the Gateway using JWKS discovery.
All routes have JWT enforced by default; health endpoints are exempted
at registration time.
"""

from __future__ import annotations

from typing import Any

import jwt
from fastapi import Request, Response
from fastapi.responses import JSONResponse
from starlette.middleware.base import BaseHTTPMiddleware
from starlette.types import ASGIApp

from app.middleware.jwks import CachedJWKSClient


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
        self._jwks_client: CachedJWKSClient | None = None

    async def dispatch(self, request: Request, call_next: Any) -> Response:
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
                    "detail": "Missing or malformed Authorization header. Expected 'Bearer <token>'.",
                    "trace_id": "",
                },
                headers={"Content-Type": "application/problem+json"},
            )

        token = auth_header.removeprefix("Bearer ")

        # Lazy init JWKS client on first request
        if self._jwks_client is None:
            self._jwks_client = CachedJWKSClient(self.jwks_url)

        try:
            # Decode JWT without verification first to get kid
            unverified_header = jwt.get_unverified_header(token)
            kid = unverified_header.get("kid")

            if not kid:
                return JSONResponse(
                    status_code=401,
                    content={
                        "type": "about:blank",
                        "title": "Unauthorized",
                        "status": 401,
                        "detail": "JWT missing 'kid' header. Cannot resolve signing key.",
                        "trace_id": "",
                    },
                    headers={"Content-Type": "application/problem+json"},
                )

            # Get the signing key from JWKS
            signing_key = await self._jwks_client.get_signing_key_from_data(kid)
            if signing_key is None:
                return JSONResponse(
                    status_code=401,
                    content={
                        "type": "about:blank",
                        "title": "Unauthorized",
                        "status": 401,
                        "detail": f"Signing key for kid='{kid}' not found in JWKS.",
                        "trace_id": "",
                    },
                    headers={"Content-Type": "application/problem+json"},
                )

            # Validate the JWT
            payload: dict[str, Any] = jwt.decode(
                token,
                signing_key,
                algorithms=["RS256"],
                audience=self.audience,
                issuer=self.issuer,
                options={
                    "verify_exp": True,
                    "verify_iat": True,
                    "require": ["exp", "iat", "aud", "iss", "sub"],
                },
            )

            request.state.user = {
                "sub": payload.get("sub", ""),
                "scope": payload.get("scope", ""),
                "token_use": payload.get("token_use", ""),
            }

        except jwt.ExpiredSignatureError:
            return JSONResponse(
                status_code=401,
                content={
                    "type": "about:blank",
                    "title": "Unauthorized",
                    "status": 401,
                    "detail": "JWT has expired.",
                    "trace_id": "",
                },
                headers={"Content-Type": "application/problem+json"},
            )
        except jwt.InvalidAudienceError:
            return JSONResponse(
                status_code=401,
                content={
                    "type": "about:blank",
                    "title": "Unauthorized",
                    "status": 401,
                    "detail": f"JWT audience does not match '{self.audience}'.",
                    "trace_id": "",
                },
                headers={"Content-Type": "application/problem+json"},
            )
        except jwt.InvalidIssuerError:
            return JSONResponse(
                status_code=401,
                content={
                    "type": "about:blank",
                    "title": "Unauthorized",
                    "status": 401,
                    "detail": f"JWT issuer does not match '{self.issuer}'.",
                    "trace_id": "",
                },
                headers={"Content-Type": "application/problem+json"},
            )
        except jwt.PyJWTError as e:
            return JSONResponse(
                status_code=401,
                content={
                    "type": "about:blank",
                    "title": "Unauthorized",
                    "status": 401,
                    "detail": f"JWT validation failed: {str(e)}",
                    "trace_id": "",
                },
                headers={"Content-Type": "application/problem+json"},
            )

        return await call_next(request)