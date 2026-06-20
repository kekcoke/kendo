"""Async PostgreSQL connection pool with read-only fastapi_ro role.

Wrapped with pybreaker circuit breaker and tenacity retry for resilience.
"""

from __future__ import annotations

import asyncpg
import pybreaker
from asyncpg import Pool

from app.resilience.circuit_breaker import get_breaker_state
from app.resilience.retry import retry_db

_pool: Pool | None = None
_pgvector_breaker: pybreaker.CircuitBreaker | None = None


@retry_db()
async def create_pool(dsn: str, min_size: int = 1, max_size: int = 5) -> Pool:
    """Create a read-only asyncpg connection pool.

    Connection uses the fastapi_ro role which has SELECT-only grants.
    Wrapped with tenacity retry for transient DB failures.
    """
    global _pool
    _pool = await asyncpg.create_pool(
        dsn=dsn,
        min_size=min_size,
        max_size=max_size,
        timeout=30,
        command_timeout=30,
    )
    return _pool


def set_pgvector_breaker(breaker: pybreaker.CircuitBreaker) -> None:
    """Set the pgvector circuit breaker instance from main.py."""
    global _pgvector_breaker
    _pgvector_breaker = breaker


async def get_pool() -> Pool:
    """Return the existing pool or raise if not initialized."""
    global _pgvector_breaker

    # Check circuit breaker before acquiring pool
    if _pgvector_breaker and _pgvector_breaker.state == pybreaker.STATE_OPEN:
        raise RuntimeError("pgvector circuit breaker is open. Dependency unavailable.")

    if _pool is None:
        raise RuntimeError("Database pool not initialized. Call create_pool() first.")

    try:
        return _pool
    except Exception:
        if _pgvector_breaker:
            _pgvector_breaker.fail()
        raise


async def close_pool() -> None:
    """Close the database connection pool."""
    global _pool
    if _pool:
        await _pool.close()
        _pool = None


@retry_db()
async def check_connection() -> bool:
    """Verify that the pool can execute a simple query."""
    global _pgvector_breaker
    try:
        pool = await get_pool()
        async with pool.acquire() as conn:
            result = await conn.fetchval("SELECT 1")
            if _pgvector_breaker:
                _pgvector_breaker.succeed()
            return result == 1
    except Exception:
        if _pgvector_breaker:
            _pgvector_breaker.fail()
        raise
