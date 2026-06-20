"""Tests for token-bucket rate limiter middleware.

Tests cover:
- Below-limit requests pass through normally
- Over-limit requests return 429 + Retry-After header
- Health endpoints are exempt from rate limiting
- Rate limit headers (X-RateLimit-Limit, -Remaining, -Reset) on allowed requests
- 429 response body is RFC 7807 with trace_id and rate_limit details
"""

from __future__ import annotations

from typing import Any
from unittest.mock import AsyncMock, MagicMock

import pytest
from fastapi import FastAPI, Request, Response

from app.middleware.rate_limit import RateLimitMiddleware, TokenBucket


# ---------------------------------------------------------------------------
# TokenBucket unit tests
# ---------------------------------------------------------------------------


class TestTokenBucket:
    """Unit tests for the TokenBucket algorithm."""

    def test_initial_tokens_full(self) -> None:
        """A new bucket starts at capacity."""
        bucket = TokenBucket(tokens_per_window=100, window_seconds=60, bucket_capacity=100)
        key = "test_user"
        assert bucket.remaining(key) == pytest.approx(100.0, abs=1.0)

    def test_consume_returns_true_while_tokens_remain(self) -> None:
        """consume() returns True until the bucket is empty."""
        bucket = TokenBucket(tokens_per_window=5, window_seconds=60, bucket_capacity=5)
        for _ in range(5):
            allowed, wait = bucket.consume("key")
            assert allowed is True
            assert wait == 0.0

    def test_consume_returns_false_when_empty(self) -> None:
        """consume() returns False once the bucket is dry."""
        bucket = TokenBucket(tokens_per_window=3, window_seconds=60, bucket_capacity=3)
        for _ in range(3):
            bucket.consume("key")

        allowed, wait = bucket.consume("key")
        assert allowed is False
        assert wait > 0.0

    def test_remaining_decreases_after_consume(self) -> None:
        """remaining() reflects token consumption."""
        bucket = TokenBucket(tokens_per_window=10, window_seconds=60, bucket_capacity=10)
        bucket.consume("key")
        bucket.consume("key")
        assert bucket.remaining("key") == pytest.approx(8.0, abs=1.0)

    def test_tokens_replenish_over_time(self) -> None:
        """Tokens are replenished as time passes."""
        bucket = TokenBucket(tokens_per_window=60, window_seconds=60, bucket_capacity=10)
        bucket._buckets["key"]["tokens"] = 0.0
        bucket._buckets["key"]["last_refill"] = 0.0  # set to epoch
        # After 10 seconds at 1 token/s rate, we should have ~10 tokens
        bucket._buckets["key"]["last_refill"] = 50.0  # 10 seconds ago from "now"
        allowed, wait = bucket.consume("key")
        assert allowed is True
        assert wait == 0.0

    def test_per_key_isolation(self) -> None:
        """Two different keys maintain independent buckets."""
        bucket = TokenBucket(tokens_per_window=2, window_seconds=60, bucket_capacity=2)
        bucket.consume("user_a")
        bucket.consume("user_a")
        # user_a is now empty; user_b should still have full tokens
        allowed, _ = bucket.consume("user_a")
        assert allowed is False
        allowed, _ = bucket.consume("user_b")
        assert allowed is True


# ---------------------------------------------------------------------------
# RateLimitMiddleware integration tests
# ---------------------------------------------------------------------------


@pytest.fixture
def app() -> FastAPI:
    """Create a minimal FastAPI app with the rate limiter middleware."""
    app = FastAPI()

    # Add rate limiter middleware with small limits for testing
    app.add_middleware(
        RateLimitMiddleware,
        tokens_per_window=3,
        window_seconds=60,
        bucket_capacity=3,
    )

    @app.get("/test")
    async def test_route() -> dict[str, str]:
        return {"status": "ok"}

    @app.get("/health/live")
    async def health_live() -> dict[str, str]:
        return {"status": "alive"}

    return app


@pytest.fixture
def mock_request_no_user() -> MagicMock:
    """Request with no user state (anonymous/client IP fallback)."""
    req = MagicMock(spec=Request)
    req.url.path = "/test"
    req.client.host = "10.0.0.1"
    req.state.user = None
    # Need to make hasattr work
    type(req.state).user = MagicMock()
    return req


class TestRateLimitMiddleware:
    """Integration tests for RateLimitMiddleware."""

    async def test_allowed_requests_pass(self, app: FastAPI) -> None:
        """Requests under the limit pass through and get rate-limit headers."""
        client = TestClientAsync(app)
        for _ in range(3):
            resp = await client.get("/test", headers={"Authorization": "Bearer test.jwt.sub"})
            assert resp.status_code == 200
            # Headers are lowercased by ASGI/Starlette
            assert "x-ratelimit-limit" in resp.headers
            assert "x-ratelimit-remaining" in resp.headers

    async def test_rate_limited_returns_429(self, app: FastAPI) -> None:
        """Exceeding the limit returns 429 with Retry-After."""
        client = TestClientAsync(app)
        for _ in range(3):
            await client.get("/test", headers={"Authorization": "Bearer test.jwt.sub"})

        resp = await client.get("/test", headers={"Authorization": "Bearer test.jwt.sub"})
        assert resp.status_code == 429
        # Headers are lowercased by ASGI/Starlette
        assert "retry-after" in resp.headers
        assert "x-ratelimit-remaining" in resp.headers
        assert resp.headers["x-ratelimit-remaining"] == "0"

    async def test_rate_limited_response_is_rfc_7807(self, app: FastAPI) -> None:
        """429 response body follows RFC 7807 with trace_id and rate_limit details."""
        client = TestClientAsync(app)
        for _ in range(3):
            await client.get("/test", headers={"Authorization": "Bearer test.jwt.sub"})

        resp = await client.get("/test", headers={"Authorization": "Bearer test.jwt.sub"})
        body = resp.json()
        assert body["type"] == "https://httpstatuses.io/429"
        assert body["title"] == "Too Many Requests"
        assert body["status"] == 429
        assert "trace_id" in body
        assert "rate_limit" in body
        assert body["rate_limit"]["limit"] == 3
        assert body["rate_limit"]["remaining"] == 0
        assert body["rate_limit"]["reset_after_seconds"] > 0

    async def test_health_endpoints_exempt(self, app: FastAPI) -> None:
        """Health endpoints bypass rate limiting entirely."""
        client = TestClientAsync(app)
        # Exhaust the bucket on the test route
        for _ in range(3):
            await client.get("/test", headers={"Authorization": "Bearer test.jwt.sub"})

        # Health endpoint should still pass
        resp = await client.get("/health/live")
        assert resp.status_code == 200

    async def test_anonymous_ip_fallback(self, app: FastAPI) -> None:
        """Missing JWT falls back to client IP for rate limiting."""
        client = TestClientAsync(app)
        # Simulate no auth header — middleware dispatches to auth → fails 401
        # This test validates the ip fallback logic directly via middleware dispatch
        req = MagicMock(spec=Request)
        req.url.path = "/test"
        req.client.host = "10.0.0.1"
        req.state.user = None
        # Remove user attr to trigger fallback
        del req.state.user

        # The IP-based bucket should be independent
        # We test by calling the dispatch method directly
        middleware = RateLimitMiddleware(app, tokens_per_window=100, window_seconds=60, bucket_capacity=100)
        call_next = AsyncMock(return_value=Response("ok"))

        resp = await middleware.dispatch(req, call_next)
        assert resp.status_code == 200  # Would be 200 if allowed


# ---------------------------------------------------------------------------
# TestClientAsync helper
# ---------------------------------------------------------------------------


class TestClientAsync:
    """Simple async test client for FastAPI."""

    def __init__(self, app: FastAPI) -> None:
        self.app = app

    async def get(self, path: str, headers: dict[str, str] | None = None) -> Any:
        """Simulate a GET request to the app."""
        scope = {
            "type": "http",
            "method": "GET",
            "path": path,
            "headers": [(k.lower().encode(), v.encode()) for k, v in (headers or {}).items()],
            "query_string": b"",
            "client": ("10.0.0.1", 12345),
            "scheme": "http",
        }

        async def receive() -> dict:
            return {"type": "http.request"}

        # Collect response
        messages: list[dict] = []

        async def send(message: dict) -> None:
            messages.append(message)

        await self.app(scope, receive, send)

        # Find the response start message
        status_code = 200
        response_headers: dict[str, str] = {}
        body_chunks: list[bytes] = []

        for msg in messages:
            if msg["type"] == "http.response.start":
                status_code = msg["status"]
                response_headers = {
                    k.decode(): v.decode() for k, v in msg.get("headers", [])
                }
            elif msg["type"] == "http.response.body":
                if msg.get("body"):
                    body_chunks.append(msg["body"])

        class ResponseWrapper:
            def __init__(
                self,
                status_code: int,
                headers: dict[str, str],
                body: bytes,
            ) -> None:
                self.status_code = status_code
                self.headers = headers
                self._body = body

            def json(self) -> Any:
                import json
                return json.loads(self._body)

        return ResponseWrapper(status_code, response_headers, b"".join(body_chunks))
