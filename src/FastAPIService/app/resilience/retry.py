"""Retry decorators — tenacity with exponential backoff + jitter.

Provides factory functions that return preconfigured tenacity retry decorators
for database operations and LLM HTTP calls.
"""

from __future__ import annotations

import logging
from typing import Callable, Type

import asyncpg
import httpx
from tenacity import (
    before_sleep_log,
    retry,
    retry_if_exception_type,
    stop_after_attempt,
    wait_exponential_jitter,
)

logger = logging.getLogger(__name__)

# Type aliases for retryable exception types
DB_EXCEPTIONS: tuple[Type[Exception], ...] = (
    asyncpg.exceptions.PostgresError,
    asyncpg.exceptions.ConnectionDoesNotExistError,
    asyncpg.exceptions.CannotConnectNowError,
    asyncpg.exceptions.InterfaceError,
    OSError,  # Network-level failures
)

LLM_EXCEPTIONS: tuple[Type[Exception], ...] = (
    httpx.TimeoutException,
    httpx.ConnectError,
    httpx.RemoteProtocolError,
    httpx.HTTPStatusError,
)


def retry_db(
    max_attempts: int = 3,
    min_wait: float = 1.0,
    max_wait: float = 30.0,
    jitter: float = 0.5,
) -> Callable:
    """Return a tenacity retry decorator for database operations.

    Args:
        max_attempts: Maximum retry attempts (default: 3).
        min_wait: Minimum wait between retries (default: 1.0s).
        max_wait: Maximum wait between retries (default: 30.0s).
        jitter: Random jitter to add/subtract (default: 0.5s).

    Returns:
        Tenacity retry decorator configured for DB exception types.
    """
    return retry(
        stop=stop_after_attempt(max_attempts),
        wait=wait_exponential_jitter(initial=min_wait, max=max_wait, jitter=jitter),
        retry=retry_if_exception_type(DB_EXCEPTIONS),
        before_sleep=before_sleep_log(logger, logging.WARNING),
        reraise=True,
    )


def retry_llm(
    max_attempts: int = 3,
    min_wait: float = 1.0,
    max_wait: float = 30.0,
    jitter: float = 0.5,
) -> Callable:
    """Return a tenacity retry decorator for LLM HTTP calls.

    Does NOT retry on 4xx responses — only on transient failures
    (timeout, connection refused, 5xx).

    Args:
        max_attempts: Maximum retry attempts (default: 3).
        min_wait: Minimum wait between retries (default: 1.0s).
        max_wait: Maximum wait between retries (default: 30.0s).
        jitter: Random jitter to add/subtract (default: 0.5s).

    Returns:
        Tenacity retry decorator configured for LLM exception types.
    """
    return retry(
        stop=stop_after_attempt(max_attempts),
        wait=wait_exponential_jitter(initial=min_wait, max=max_wait, jitter=jitter),
        retry=retry_if_exception_type(LLM_EXCEPTIONS),
        before_sleep=before_sleep_log(logger, logging.WARNING),
        reraise=True,
    )
