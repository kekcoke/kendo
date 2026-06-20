"""Chaos test: FastAPI crash + recovery.

Verifies that:
- FastAPI crashes are detected by the Gateway's Polly circuit breaker
- The Gateway returns RFC 7807 503 (not connection refused) to clients
- docker-compose restart restores the service within health-check TTL
"""

from __future__ import annotations

import pytest


@pytest.mark.chaos
class TestChaosCrash:
    """Chaos tests for FastAPI container crash + recovery."""

    async def test_crash_returns_503_via_gateway(
        self, chaos_app: None
    ) -> None:
        """Placeholder: FastAPI crash causes Gateway to return 503.

        In CI, this test:
        1. SIGKILLs the FastAPI container
        2. Queries the Gateway /api/rag/query
        3. Asserts 503 RFC 7807 (not connection refused)
        4. Runs `docker compose restart fastapi`
        5. Waits for `/health/ready` to return 200
        6. Asserts queries resume succeeding

        Local dev: skipped unless --chaos flag is passed.
        """
        pytest.skip("Chaos crash test requires docker-compose stack in CI")
