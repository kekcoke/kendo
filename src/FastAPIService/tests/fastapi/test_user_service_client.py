"""Unit tests for UserServiceClient (W5 backfill integration).

Tests cover:
- get_events: success, error handling, circuit breaker
- write_embeddings_batch: success, error handling, circuit breaker
- UserServiceClientError on non-retryable failures
"""

from __future__ import annotations

from unittest.mock import Mock, patch

import httpx
import pytest

from app.integrations.user_service_client import UserServiceClient, UserServiceClientError


# =============================================================================
# UserServiceClient — get_events
# =============================================================================

class TestGetEvents:
    """Tests for UserServiceClient.get_events."""

    @pytest.mark.asyncio
    async def test_success_returns_events(self) -> None:
        """200 response returns parsed events list."""
        events = [{"id": "evt-1", "name": "Test"}]
        client = UserServiceClient(base_url="http://test:5001", timeout_seconds=5)

        with patch("app.integrations.user_service_client.httpx.Client") as mock_http_cls:
            mock_http = Mock()
            mock_resp = Mock()
            mock_resp.status_code = 200
            mock_resp.json.return_value = events
            mock_http.get.return_value = mock_resp
            mock_http_cls.return_value.__enter__.return_value = mock_http

            result = await client.get_events(since="2026-01-01", limit=64)

        assert result == events

    @pytest.mark.asyncio
    async def test_500_retries_then_opens_breaker(self) -> None:
        """500 after retries trips circuit breaker -> UserServiceClientError."""
        client = UserServiceClient(base_url="http://test:5001", timeout_seconds=5)

        with patch("app.integrations.user_service_client.httpx.Client") as mock_http_cls:
            mock_http = Mock()
            mock_resp = Mock()
            mock_resp.status_code = 500
            mock_resp.text = "Internal Server Error"
            mock_resp.raise_for_status.side_effect = httpx.HTTPStatusError(
                "500", request=Mock(), response=mock_resp,
            )
            mock_http.get.return_value = mock_resp
            mock_http_cls.return_value.__enter__.return_value = mock_http

            # 3 retries + 3 actual calls = breaker trips after 3 failures
            with pytest.raises(UserServiceClientError, match="circuit breaker is open"):
                await client.get_events(since="2026-01-01", limit=64)

    @pytest.mark.asyncio
    async def test_400_raises_client_error(self) -> None:
        """400 raises UserServiceClientError (non-retryable)."""
        client = UserServiceClient(base_url="http://test:5001", timeout_seconds=5)

        with patch("app.integrations.user_service_client.httpx.Client") as mock_http_cls:
            mock_http = Mock()
            mock_resp = Mock()
            mock_resp.status_code = 400
            mock_resp.text = "Bad Request"
            mock_resp.raise_for_status.side_effect = httpx.HTTPStatusError(
                "400", request=Mock(), response=mock_resp,
            )
            mock_http.get.return_value = mock_resp
            mock_http_cls.return_value.__enter__.return_value = mock_http

            with pytest.raises(UserServiceClientError):
                await client.get_events(since="2026-01-01", limit=64)

    @pytest.mark.asyncio
    async def test_circuit_breaker_open_raises_client_error(self) -> None:
        """Open circuit breaker raises UserServiceClientError."""
        client = UserServiceClient(base_url="http://test:5001", timeout_seconds=5)

        # Trip the breaker by triggering 3 failures
        with patch("app.integrations.user_service_client.httpx.Client") as mock_http_cls:
            mock_http = Mock()
            mock_resp = Mock()
            mock_resp.status_code = 500
            mock_resp.text = "error"
            mock_resp.raise_for_status.side_effect = httpx.HTTPStatusError(
                "500", request=Mock(), response=mock_resp,
            )
            mock_http.get.return_value = mock_resp
            mock_http_cls.return_value.__enter__.return_value = mock_http

            # After 3 retry attempts + 3 original calls, breaker trips
            with pytest.raises(UserServiceClientError, match="circuit breaker is open"):
                await client.get_events(since="2026-01-01", limit=64)


# =============================================================================
# UserServiceClient — write_embeddings_batch
# =============================================================================

class TestWriteEmbeddingsBatch:
    """Tests for UserServiceClient.write_embeddings_batch."""

    @pytest.mark.asyncio
    async def test_success_returns_job_id(self) -> None:
        """202 response returns job details."""
        expected = {"job_id": "job-1", "processed": 5, "status": "accepted"}
        client = UserServiceClient(base_url="http://test:5001", timeout_seconds=5)
        payload = [
            {"target": "event", "targetId": "evt-1", "modelName": "m1",
             "dimensions": 128, "embedding": [0.1] * 128},
        ]

        with patch("app.integrations.user_service_client.httpx.Client") as mock_http_cls:
            mock_http = Mock()
            mock_resp = Mock()
            mock_resp.status_code = 202
            mock_resp.json.return_value = expected
            mock_http.post.return_value = mock_resp
            mock_http_cls.return_value.__enter__.return_value = mock_http

            result = await client.write_embeddings_batch(payload)

        assert result == expected

    @pytest.mark.asyncio
    async def test_500_retries_then_opens_breaker(self) -> None:
        """500 after retries trips circuit breaker -> UserServiceClientError."""
        client = UserServiceClient(base_url="http://test:5001", timeout_seconds=5)
        payload = [
            {"target": "event", "targetId": "evt-1", "modelName": "m1",
             "dimensions": 128, "embedding": [0.1]},
        ]

        with patch("app.integrations.user_service_client.httpx.Client") as mock_http_cls:
            mock_http = Mock()
            mock_resp = Mock()
            mock_resp.status_code = 500
            mock_resp.text = "Batch failed"
            mock_resp.raise_for_status.side_effect = httpx.HTTPStatusError(
                "500", request=Mock(), response=mock_resp,
            )
            mock_http.post.return_value = mock_resp
            mock_http_cls.return_value.__enter__.return_value = mock_http

            # 3 retries + 3 actual calls = breaker trips after 3 failures
            with pytest.raises(UserServiceClientError, match="circuit breaker is open"):
                await client.write_embeddings_batch(payload)
