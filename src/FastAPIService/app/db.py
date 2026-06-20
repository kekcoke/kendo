"""Async PostgreSQL connection pool with read-only fastapi_ro role."""

from __future__ import annotations

import asyncpg
from asyncpg import Pool

_pool: Pool | None = None


async def create_pool(dsn: str, min_size: int = 1, max_size: int = 5) -> Pool:
    """Create a read-only asyncpg connection pool.

    Connection uses the fastapi_ro role which has SELECT-only grants.
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


async def get_pool() -> Pool:
    """Return the existing pool or raise if not initialized."""
    if _pool is None:
        raise RuntimeError("Database pool not initialized. Call create_pool() first.")
    return _pool


async def close_pool() -> None:
    """Close the database connection pool."""
    global _pool
    if _pool:
        await _pool.close()
        _pool = None


async def check_connection() -> bool:
    """Verify that the pool can execute a simple query."""
    try:
        pool = await get_pool()
        async with pool.acquire() as conn:
            result = await conn.fetchval("SELECT 1")
            return result == 1
    except Exception:
        return False
