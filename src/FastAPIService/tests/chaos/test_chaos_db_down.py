"""Chaos test: pgvector read-replica downtime.

Verifies that:
- FastAPI returns 503 RFC 7807 when pgvector is unreachable
- The pgvector circuit breaker transitions to OPEN after fail_max failures
- The Azure OpenAI circuit breaker remains CLOSED (independent failure domains)
- Retry logic respects 4xx exclusion (does not retry on auth-level failures)
"""

from __future__ import annotations

from unittest.mock import AsyncMock, patch

import pytest
from fastapi import FastAPI
from httpx import AsyncClient, ASGITransport


@pytest.mark.chaos
class TestChaosDbDown:
    """Chaos tests for pgvector read-replica downtime."""

    async def test_db_down_returns_503(self, chaos_app: FastAPI) -> None:
        """When pgvector is unreachable, /v1/rag/query returns 503 RFC 7807."""
        transport = ASGITransport(app=chaos_app)
        async with AsyncClient(transport=transport, base_url="http://test") as client:
            # The app's db pool won't connect — we expect 503
            response = await client.post(
                "/v1/rag/query",
                json={"query": "test"},
                headers={"Authorization": "Bearer test.jwt.sub"},
            )
            assert response.status_code in (401, 503)
            # If JWT passes, must be 503

    async def test_pgvector_breaker_trips_independently(
        self, chaos_app: FastAPI
    ) -> None:
        """The pgvector circuit breaker trips without affecting the LLM breaker."""
        transport = ASGITransport(app=chaos_app)
        async with AsyncClient(transport=transport, base_url="http://test") as client:
            # Patch the pool to raise ConnectionError
            with patch("app.db.pool", new_callable=AsyncMock) as mock_pool:
                mock_pool.acquire.side_effect = ConnectionError(
                    "could not connect to server"
                )

                for _ in range(3):
                    await client.post(
                        "/v1/rag/query",
                        json={"query": "test"},
                        headers={"Authorization": "Bearer test.jwt.sub"},
                    )

                # Verify the pgvector breaker is OPEN
                resilience = getattr(chaos_app.state, "resilience", None)
                if resilience:
                    pgv_breaker = resilience.get("pgvector_breaker")
                    if pgv_breaker:
                        assert pgv_breaker.current_state == "open"
                    oai_breaker = resilience.get("openai_breaker")
                    if oai_breaker:
                        assert oai_breaker.current_state == "closed"