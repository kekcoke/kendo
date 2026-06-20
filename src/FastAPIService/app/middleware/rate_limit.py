"""Token-bucket rate limiter middleware.

Per-JWT-subject rate limiting with client IP fallback.
Returns 429 + Retry-After header + RFC 7807 body when exceeded.
"""

from __future__ import annotations

import time
from collections import defaultdict
from typing import Any

from fastapi import Request, Response
from fastapi.responses import JSONResponse
from starlette.middleware.base import BaseHTTPMiddleware
from starlette.types import ASGIApp

from app.observability.tracing import get_current_trace_id


class TokenBucket:
    """A simple per-key token bucket with burst capacity.

    Thread-safety is not required because FastAPI middleware runs
    in an async context; the defaultdict + per-key state is single-threaded
    within a request (ASGI lifespan is serial per-request).
    """

    __slots__ = ("_tokens_per_window", "_window_seconds", "_bucket_capacity", "_buckets")

    def __init__(
        self,
        tokens_per_window: int = 100,
        window_seconds: int = 60,
        bucket_capacity: int = 100,
    ) -> None:
        self._tokens_per_window = tokens_per_window
        self._window_seconds = window_seconds
        self._bucket_capacity = bucket_capacity
        self._buckets: dict[str, dict[str, float | int]] = defaultdict(
            lambda: {"tokens": float(bucket_capacity), "last_refill": time.monotonic()}
        )

    def _refill(self, key: str) -> None:
        """Refill tokens based on elapsed time."""
        bucket = self._buckets[key]
        now = time.monotonic()
        elapsed = now - bucket["last_refill"]
        # Tokens accumulated since last refill
        rate = self._tokens_per_window / self._window_seconds
        new_tokens = elapsed * rate
        bucket["tokens"] = min(float(self._bucket_capacity), bucket["tokens"] + new_tokens)
        bucket["last_refill"] = now

    def consume(self, key: str) -> tuple[bool, float]:
        """Try to consume one token.

        Returns:
            (allowed, wait_seconds): whether the request is allowed and
            how many seconds until the next token is available.
        """
        self._refill(key)
        bucket = self._buckets[key]

        if bucket["tokens"] >= 1.0:
            bucket["tokens"] -= 1.0
            return True, 0.0

        # Calculate seconds until next token
        rate = self._tokens_per_window / self._window_seconds
        if rate > 0:
            wait = (1.0 - bucket["tokens"]) / rate
        else:
            wait = self._window_seconds

        return False, max(1.0, wait)

    def remaining(self, key: str) -> float:
        """Get remaining tokens for a key (without consuming)."""
        self._refill(key)
        return self._buckets[key]["tokens"]


class RateLimitMiddleware(BaseHTTPMiddleware):
    """Token-bucket rate limiter keyed by JWT subject (or client IP).

    Health endpoints are exempted via EXEMPT_PATHS.
    """

    EXEMPT_PATHS: tuple[str, ...] = ("/health/live", "/health/ready")

    def __init__(
        self,
        app: ASGIApp,
        tokens_per_window: int = 100,
        window_seconds: int = 60,
        bucket_capacity: int = 100,
    ) -> None:
        super().__init__(app)
        self._tokens_per_window = tokens_per_window
        self._window_seconds = window_seconds
        self._bucket_capacity = bucket_capacity
        self._bucket = TokenBucket(
            tokens_per_window=tokens_per_window,
            window_seconds=window_seconds,
            bucket_capacity=bucket_capacity,
        )

    async def dispatch(self, request: Request, call_next: Any) -> Response:
        # Exempt health endpoints
        if request.url.path in self.EXEMPT_PATHS:
            return await call_next(request)

        # Determine the rate-limit key: JWT sub -> client IP fallback
        key: str | None = None
        if hasattr(request.state, "user") and request.state.user:
            key = request.state.user.get("sub")

        if not key:
            key = request.client.host if request.client else "unknown"

        allowed, wait = self._bucket.consume(key)
        remaining = int(self._bucket.remaining(key))
        reset_after = int(wait)

        if allowed:
            response = await call_next(request)
            # Attach rate-limit headers
            response.headers["X-RateLimit-Limit"] = str(self._tokens_per_window)
            response.headers["X-RateLimit-Remaining"] = str(max(0, remaining))
            response.headers["X-RateLimit-Reset"] = str(reset_after)
            return response

        # Rate limit exceeded — return 429 RFC 7807
        return JSONResponse(
            status_code=429,
            content={
                "type": "https://httpstatuses.io/429",
                "title": "Too Many Requests",
                "status": 429,
                "detail": f"Rate limit exceeded. Retry after {reset_after} seconds.",
                "trace_id": get_current_trace_id(),
                "rate_limit": {
                    "limit": self._tokens_per_window,
                    "window_seconds": self._window_seconds,
                    "remaining": 0,
                    "reset_after_seconds": reset_after,
                },
            },
            headers={
                "Content-Type": "application/problem+json",
                "Retry-After": str(reset_after),
                "X-RateLimit-Limit": str(self._tokens_per_window),
                "X-RateLimit-Remaining": "0",
                "X-RateLimit-Reset": str(reset_after),
            },
        )
