"""User profile search repository — hybrid pgvector cosine + BM25.

Combines pgvector cosine similarity (semantic) with Postgres full-text
search using ts_rank (BM25-like) for hybrid retrieval against user_embeddings.
"""

from __future__ import annotations

from typing import Any

from app.db import get_pool
from app.rag.embeddings_client import BaseEmbeddingClient


async def cosine_search(
    embedding: list[float],
    top_k: int = 10,
) -> list[dict[str, Any]]:
    """Search user_embeddings by cosine distance (semantic search).

    Uses pgvector <=> (cosine distance) operator.
    All reads go through the fastapi_ro role.
    """
    pool = await get_pool()

    query = """
        SELECT
            ue."UserId",
            1 - (ue."Embedding" <=> $1::vector) AS score,
            ue."ModelName"
        FROM user_embeddings ue
        ORDER BY ue."Embedding" <=> $1::vector
        LIMIT $2
    """

    async with pool.acquire() as conn:
        rows = await conn.fetch(query, embedding, top_k)

    return [
        {
            "user_id": str(row["UserId"]),
            "score": float(row["score"]),
            "model": row["ModelName"],
        }
        for row in rows
    ]


async def bm25_search(
    query_text: str,
    top_k: int = 10,
) -> list[dict[str, Any]]:
    """Search user profiles using Postgres full-text search (BM25-like).

    Uses ts_rank on a tsvector built from user profile data.
    Falls back to ILIKE on display_name if users table has no tsvector column.
    """
    pool = await get_pool()

    # Attempt tsvector search first (more accurate), fall back to ILIKE
    query = """
        SELECT
            ue."UserId",
            ts_rank(
                to_tsvector('english', COALESCE(u."DisplayName", '') || ' ' ||
                            COALESCE(u."Bio", '') || ' ' ||
                            COALESCE(u."Email", '')),
                plainto_tsquery('english', $1::text)
            ) AS score,
            ue."ModelName"
        FROM user_embeddings ue
        JOIN "Users" u ON u."Id" = ue."UserId"
        WHERE
            to_tsvector('english', COALESCE(u."DisplayName", '') || ' ' ||
                        COALESCE(u."Bio", '') || ' ' ||
                        COALESCE(u."Email", '')) @@ plainto_tsquery('english', $1::text)
        ORDER BY score DESC
        LIMIT $2
    """

    async with pool.acquire() as conn:
        rows = await conn.fetch(query, query_text, top_k)

    if rows:
        return [
            {
                "user_id": str(row["UserId"]),
                "score": float(row["score"]),
                "model": row["ModelName"],
            }
            for row in rows
        ]

    # Fallback: ILIKE search on display_name
    fallback_query = """
        SELECT
            ue."UserId",
            0.5 AS score,
            ue."ModelName"
        FROM user_embeddings ue
        JOIN "Users" u ON u."Id" = ue."UserId"
        WHERE u."DisplayName" ILIKE '%' || $1::text || '%'
        LIMIT $2
    """

    async with pool.acquire() as conn:
        rows = await conn.fetch(fallback_query, query_text, top_k)

    return [
        {
            "user_id": str(row["UserId"]),
            "score": float(row["score"]),
            "model": row["ModelName"],
        }
        for row in rows
    ]


async def hybrid_search(
    query_text: str,
    embedding_client: BaseEmbeddingClient,
    top_k: int = 10,
    cosine_weight: float = 0.7,
    bm25_weight: float = 0.3,
) -> list[dict[str, Any]]:
    """Run hybrid search combining cosine semantic + BM25 keyword scores.

    Args:
        query_text: The user's search query string.
        embedding_client: Client to compute query embedding.
        top_k: Number of results to return.
        cosine_weight: Weight for cosine similarity score (default 0.7).
        bm25_weight: Weight for BM25 full-text score (default 0.3).

    Returns:
        List of merged and top-K scored results with user_id, score, model.
        Empty list if both searches fail.
    """
    # Compute embedding for semantic search
    embedding = await embedding_client.embed_query(query_text)

    # Run both searches in parallel
    import asyncio
    cosine_results, bm25_results = await asyncio.gather(
        cosine_search(embedding, top_k=top_k * 2),
        bm25_search(query_text, top_k=top_k * 2),
        return_exceptions=True,
    )

    # Handle individual failures with graceful fallback
    if isinstance(cosine_results, Exception):
        cosine_results = []
    if isinstance(bm25_results, Exception):
        bm25_results = []

    # If both failed, return empty
    if not cosine_results and not bm25_results:
        return []

    # If one failed entirely, return the other
    if not cosine_results:
        return bm25_results[:top_k]
    if not bm25_results:
        return cosine_results[:top_k]

    # Normalize scores within each result set
    max_cosine = max((r["score"] for r in cosine_results), default=1.0)
    max_bm25 = max((r["score"] for r in bm25_results), default=1.0)

    # Merge and weight scores by user_id
    merged: dict[str, dict[str, Any]] = {}
    for r in cosine_results:
        merged[r["user_id"]] = {
            "user_id": r["user_id"],
            "score": (r["score"] / max_cosine) * cosine_weight,
            "model": r["model"],
        }

    for r in bm25_results:
        if r["user_id"] in merged:
            merged[r["user_id"]]["score"] += (r["score"] / max_bm25) * bm25_weight
        else:
            merged[r["user_id"]] = {
                "user_id": r["user_id"],
                "score": (r["score"] / max_bm25) * bm25_weight,
                "model": r["model"],
            }

    # Sort by combined score descending and return top_k
    results = sorted(merged.values(), key=lambda x: x["score"], reverse=True)
    return results[:top_k]
