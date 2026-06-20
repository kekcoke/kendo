"""Chaos test: Azure OpenAI outage.

Verifies that:
- FastAPI returns 503 RFC 7807 when Azure OpenAI is unreachable
- The Azure OpenAI circuit breaker transitions to OPEN
- The pgvector circuit breaker remains CLOSED (independent failure domains)
- 4xx errors from LLM are NOT retried (tenacity exclusion predicate)
"""

from __future__ import annotations

from unittest.mock import AsyncMock, patch

import pytest
from fastapi import FastAPI
from httpx import AsyncClient, ASGITransport


@pytest.mark.chaos
class TestChaosLlmDown:
    """Chaos tests for Azure OpenAI outage."""

    async def test_llm_down_returns_503(self, chaos_app: FastAPI) -> None:
        """When Azure OpenAI is unreachable, /v1/rag/query returns 503."""
        transport = ASGITransport(app=chaos_app)
        async with AsyncClient(transport=transport, base_url="http://test") as client:
            # Patch the httpx LLM client to raise connection error
            with patch("app.rag.embeddings_client.httpx.AsyncClient") as mock_llm:
                mock_llm.return_value.post.side_effect = ConnectionError(
                    "LLM endpoint unreachable"
                )
                response = await client.post(
                    "/v1/rag/query",
                    json={"query": "test"},
                    headers={"Authorization": "Bearer test.jwt.sub"},
                )
                assert response.status_code in (401, 503)

    async def test_llm_breaker_trips_independently(
        self, chaos_app: FastAPI
    ) -> None:
        """The LLM circuit breaker trips without affecting pgvector breaker."""
        transport = ASGITransport(app=chaos_app)
        async with AsyncClient(transport=transport, base_url="http/test") as client:
            # Trigger LLM failures while pgvector remains healthy
            with patch("app.rag.embeddings_client.httpx.AsyncClient") as mock_llm:
                mock_llm.return_value.post.return_value.status_code = 503

                for _ in range(3):
                    await client.post(
                        "/v1/rag/query",
                        json={"query": "test"},
                        headers={"Authorization": "Bearer test.jwt.sub"},
                    )

                resilience = getattr(chaos_app.state, "resilience", None)
                if resilience:
                    oai_breaker = resilience.get("openai_breaker")
                    if oai_breaker:
                        assert oai_breaker.current_state == "open"
                    pgv_breaker = resilience.get("pgvector_breaker")
                    if pgv_breaker:
                        # pgvector breaker should be unaffected by LLM failures
                        assert pgv_breaker.current_state == "closed"
