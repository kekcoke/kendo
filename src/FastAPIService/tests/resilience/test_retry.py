"""Unit tests for tenacity retry decorator factories."""

from __future__ import annotations

import asyncio

import asyncpg
import httpx
import pytest

from app.resilience.retry import retry_db, retry_llm


class TestRetryDb:
    """Tests for the retry_db decorator factory."""

    def test_retry_db_decorator_created(self) -> None:
        """retry_db returns a callable decorator."""
        decorator = retry_db(max_attempts=3, min_wait=0.1, max_wait=1.0, jitter=0.0)
        assert callable(decorator)

    def test_retry_db_success_on_first_try(self) -> None:
        """Decorator does not retry on success."""
        call_count = 0

        @retry_db(max_attempts=3, min_wait=0.1, max_wait=1.0, jitter=0.0)
        async def query():
            nonlocal call_count
            call_count += 1
            return "success"

        result = asyncio.run(query())
        assert result == "success"
        assert call_count == 1

    def test_retry_db_retries_on_transient_error(self) -> None:
        """Decorator retries on asyncpg PostgresError."""
        call_count = 0

        @retry_db(max_attempts=3, min_wait=0.1, max_wait=1.0, jitter=0.0)
        async def query():
            nonlocal call_count
            call_count += 1
            if call_count < 3:
                raise asyncpg.exceptions.PostgresError("connection failed")
            return "recovered"

        result = asyncio.run(query())
        assert result == "recovered"
        assert call_count == 3

    def test_retry_db_exhausts_attempts(self) -> None:
        """Decorator retries the max_attempts and re-raises the original exception."""
        call_count = 0

        @retry_db(max_attempts=3, min_wait=0.1, max_wait=1.0, jitter=0.0)
        async def query():
            nonlocal call_count
            call_count += 1
            raise asyncpg.exceptions.PostgresError("connection failed")

        with pytest.raises(asyncpg.exceptions.PostgresError):
            asyncio.run(query())

        assert call_count == 3

    def test_retry_db_does_not_retry_value_error(self) -> None:
        """Decorator does NOT retry on non-PostgresError exceptions."""
        call_count = 0

        @retry_db(max_attempts=3, min_wait=0.1, max_wait=1.0, jitter=0.0)
        async def query():
            nonlocal call_count
            call_count += 1
            raise ValueError("invalid input")

        with pytest.raises(ValueError):
            asyncio.run(query())

        assert call_count == 1  # Only tried once


class TestRetryLlm:
    """Tests for the retry_llm decorator factory."""

    def test_retry_llm_decorator_created(self) -> None:
        """retry_llm returns a callable decorator."""
        decorator = retry_llm(max_attempts=3, min_wait=0.1, max_wait=1.0, jitter=0.0)
        assert callable(decorator)

    def test_retry_llm_retries_on_timeout(self) -> None:
        """Decorator retries on httpx.TimeoutException."""
        call_count = 0

        @retry_llm(max_attempts=3, min_wait=0.1, max_wait=1.0, jitter=0.0)
        async def call_llm():
            nonlocal call_count
            call_count += 1
            if call_count < 3:
                raise httpx.TimeoutException("LLM timed out", request=None)
            return "response"

        result = asyncio.run(call_llm())
        assert result == "response"
        assert call_count == 3

    def test_retry_llm_does_not_retry_4xx(self) -> None:
        """Decorator does NOT retry on 4xx HTTP errors."""
        call_count = 0

        @retry_llm(max_attempts=3, min_wait=0.1, max_wait=1.0, jitter=0.0)
        async def call_llm():
            nonlocal call_count
            call_count += 1
            raise httpx.HTTPStatusError(
                "400 Bad Request",
                request=httpx.Request("POST", "http://llm"),
                response=httpx.Response(400),
            )

        with pytest.raises(httpx.HTTPStatusError):
            asyncio.run(call_llm())

        assert call_count == 1

    def test_retry_llm_does_not_retry_value_error(self) -> None:
        """Decorator does NOT retry on non-httpx exceptions."""
        call_count = 0

        @retry_llm(max_attempts=3, min_wait=0.1, max_wait=1.0, jitter=0.0)
        async def call_llm():
            nonlocal call_count
            call_count += 1
            raise ValueError("unexpected error")

        with pytest.raises(ValueError):
            asyncio.run(call_llm())

        assert call_count == 1
