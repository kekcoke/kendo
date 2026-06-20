"""Embedding similarity search repository — reads pgvector via fastapi_ro."""

from __future__ import annotations

from typing import Any

from app.db import get_pool


async def similarity_search(
    embedding: list[float],
    top_k: int = 5,
    filters: dict[str, Any] | None = None,
) -> list[dict[str, Any]]:
    """Search for the top-K most similar embeddings using cosine distance.

    Uses the pgvector <=> (cosine distance) operator.
    All reads go through the fastapi_ro role — no write access.
    """
    pool = await get_pool()

    query = """
        SELECT
            ee.id,
            e.title AS text,
            1 - (ee.embedding <=> $1::vector) AS score,
            'events' AS source
        FROM event_embeddings ee
        JOIN events e ON e.id = ee.event_id
        ORDER BY ee.embedding <=> $1::vector
        LIMIT $2
    """

    async with pool.acquire() as conn:
        rows = await conn.fetch(query, embedding, top_k)

    return [
        {
            "id": str(row["id"]),
            "text": row["text"],
            "score": float(row["score"]),
            "source": row["source"],
        }
        for row in rows
    ]
