"""HTTP client for UserService admin endpoints (W5 backfill).

Used by the reindex CLI to:
  - Read events via GET /api/events?since=<ts>&limit=<n>
  - Write embeddings via POST /internal/embeddings/batch

All calls require service-JWT with admin:writes scope.
Wrapped with tenacity retry and pybreaker circuit breaker.
"""

from __future__ import annotations

import logging
from typing import Any

import httpx
import pybreaker

from app.resilience.retry import retry_llm

logger = logging.getLogger(__name__)


class UserServiceClientError(Exception):
    """Raised on non-recoverable UserService client errors."""


class UserServiceClient:
    """HTTP client for UserService admin endpoints.

    Each instance carries its own pybreaker circuit breaker for isolation.

    Args:
        base_url: Base URL for UserService (e.g., http://userservice:5001).
        timeout_seconds: HTTP request timeout.
    """

    def __init__(
        self,
        base_url: str = "http://userservice:5001",
        timeout_seconds: int = 30,
    ) -> None:
        self.base_url = base_url.rstrip("/")
        self.timeout = timeout_seconds
        self._breaker = pybreaker.CircuitBreaker(
            fail_max=3,
            reset_timeout=30,
            name="user_service_admin",
        )

    # ── Read: GET /api/events ────────────────────────────────────────────

    @retry_llm()
    async def get_events(
        self,
        since: str,
        limit: int = 64,
    ) -> list[dict[str, Any]]:
        """Fetch events from UserService.

        Args:
            since: ISO timestamp cursor.
            limit: Max events per page.

        Returns:
            List of event dicts.

        Raises:
            UserServiceClientError: On non-retryable failures.
        """

        def _do() -> list[dict[str, Any]]:
            with httpx.Client(base_url=self.base_url, timeout=self.timeout) as http:
                resp = http.get(
                    "/api/events",
                    params={"since": since, "limit": limit},
                )
                if resp.status_code == 200:
                    return resp.json()
                elif resp.status_code >= 500:
                    resp.raise_for_status()
                else:
                    raise UserServiceClientError(
                        f"UserService returned {resp.status_code}: {resp.text[:200]}",
                    )

        try:
            return self._breaker.call(_do)
        except pybreaker.CircuitBreakerError as exc:
            raise UserServiceClientError(
                "UserService circuit breaker is open. Skipping batch.",
            ) from exc

    # ── Write: POST /internal/embeddings/batch ───────────────────────────

    @retry_llm()
    async def write_embeddings_batch(
        self,
        embeddings: list[dict[str, Any]],
    ) -> dict[str, Any]:
        """Write a batch of embeddings to UserService.

        Args:
            embeddings: List of embedding payloads, each containing:
                - target: str ("event" or "user")
                - targetId: str
                - modelName: str
                - dimensions: int
                - embedding: list[float]

        Returns:
            Response dict with job_id.

        Raises:
            UserServiceClientError: On non-retryable failures.
        """

        def _do() -> dict[str, Any]:
            with httpx.Client(base_url=self.base_url, timeout=self.timeout) as http:
                resp = http.post(
                    "/internal/embeddings/batch",
                    json={"embeddings": embeddings},
                )
                if resp.status_code == 202:
                    return resp.json()
                elif resp.status_code >= 500:
                    resp.raise_for_status()
                else:
                    raise UserServiceClientError(
                        f"UserService batch write returned {resp.status_code}: {resp.text[:200]}",
                    )

        try:
            return self._breaker.call(_do)
        except pybreaker.CircuitBreakerError as exc:
            raise UserServiceClientError(
                "UserService circuit breaker is open. Cannot write embeddings.",
            ) from exc
