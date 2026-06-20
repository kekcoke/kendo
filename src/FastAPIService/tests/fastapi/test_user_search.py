"""Unit tests for W3 User Profile Semantic Search.

Tests cover:
- hybrid_search: cosine + BM25 merge logic, score normalization
- cosine_search: pgvector query shape
- bm25_search: fallback ILIKE behavior
- search_users endpoint: response shape, error handling
"""

from __future__ import annotations

from unittest.mock import AsyncMock, Mock, patch

import pytest

from app.config import Settings
from app.repositories.user_search import cosine_search, bm25_search, hybrid_search


def _settings(**overrides: str | int | bool | float) -> Settings:
    kwargs: dict = {
        "env": "test",
        "embedding_model": "test-model",
        "embedding_dimension": 128,
    }
    kwargs.update(overrides)
    return Settings(**kwargs)  # type: ignore[call-arg]


@pytest.fixture
def mock_pool():
    """Create a mock asyncpg pool that returns fake rows."""
    from unittest.mock import Mock
    conn = AsyncMock()
    conn.fetch = AsyncMock(return_value=[])

    cm = AsyncMock()
    cm.__aenter__.return_value = conn
    cm.__aexit__.return_value = None

    pool = Mock()
    pool.acquire.return_value = cm
    return pool


class TestCosineSearch:
    """Tests for pgvector cosine similarity search."""

    @pytest.mark.asyncio
    async def test_cosine_search_returns_results(self, mock_pool: AsyncMock) -> None:
        """Valid embedding returns ranked user results."""
        conn = mock_pool.acquire.return_value.__aenter__.return_value
        conn.fetch.return_value = [
            {"UserId": "550e8400-e29b-41d4-a716-446655440000", "score": 0.92, "ModelName": "test-model"},
        ]

        embedding = [0.1] * 128
        with patch("app.repositories.user_search.get_pool", return_value=mock_pool):
            results = await cosine_search(embedding, top_k=10)

        assert len(results) == 1
        assert results[0]["user_id"] == "550e8400-e29b-41d4-a716-446655440000"
        assert results[0]["score"] == 0.92

    @pytest.mark.asyncio
    async def test_cosine_search_empty(self, mock_pool: AsyncMock) -> None:
        """Empty database returns empty list."""
        conn = mock_pool.acquire.return_value.__aenter__.return_value
        conn.fetch.return_value = []

        embedding = [0.1] * 128
        with patch("app.repositories.user_search.get_pool", return_value=mock_pool):
            results = await cosine_search(embedding, top_k=10)

        assert results == []


class TestBM25Search:
    """Tests for Postgres full-text search."""

    @pytest.mark.asyncio
    async def test_bm25_search_returns_results(self, mock_pool: AsyncMock) -> None:
        """Valid query returns ranked user results via tsvector."""
        conn = mock_pool.acquire.return_value.__aenter__.return_value
        conn.fetch.return_value = [
            {"UserId": "550e8400-e29b-41d4-a716-446655440001", "score": 0.85, "ModelName": "test-model"},
        ]

        with patch("app.repositories.user_search.get_pool", return_value=mock_pool):
            results = await bm25_search("developer", top_k=10)

        assert len(results) == 1
        assert results[0]["user_id"] == "550e8400-e29b-41d4-a716-446655440001"

    @pytest.mark.asyncio
    async def test_bm25_search_empty_tsvector_falls_back_to_ilike(self, mock_pool: AsyncMock) -> None:
        """Empty tsvector results fall back to ILIKE on DisplayName."""
        conn = mock_pool.acquire.return_value.__aenter__.return_value
        conn.fetch.side_effect = [
            [],
            [
                {"UserId": "550e8400-e29b-41d4-a716-446655440002",
                 "score": 0.5, "ModelName": "test-model"},
            ],
        ]

        with patch("app.repositories.user_search.get_pool", return_value=mock_pool):
            results = await bm25_search("john", top_k=10)

        assert len(results) == 1
        assert results[0]["user_id"] == "550e8400-e29b-41d4-a716-446655440002"
        assert conn.fetch.call_count == 2


class TestHybridSearch:
    """Tests for combined cosine + BM25 search."""

    @pytest.mark.asyncio
    async def test_hybrid_search_merges_results(self) -> None:
        """Hybrid search merges and weights results."""
        embedding_client = AsyncMock()
        embedding_client.embed_query.return_value = [0.1] * 128

        cosine_results = [
            {"user_id": "user-a", "score": 0.9, "model": "test"},
            {"user_id": "user-b", "score": 0.8, "model": "test"},
        ]
        bm25_results = [
            {"user_id": "user-a", "score": 0.7, "model": "test"},
            {"user_id": "user-c", "score": 0.6, "model": "test"},
        ]

        with (
            patch("app.repositories.user_search.cosine_search", return_value=cosine_results),
            patch("app.repositories.user_search.bm25_search", return_value=bm25_results),
        ):
            results = await hybrid_search(
                "developer", embedding_client, top_k=10,
                cosine_weight=0.7, bm25_weight=0.3,
            )

        assert len(results) == 3
        assert results[0]["user_id"] == "user-a"
        assert abs(results[0]["score"] - 1.0) < 0.01
        assert results[1]["user_id"] == "user-b"
        assert results[2]["user_id"] == "user-c"

    @pytest.mark.asyncio
    async def test_hybrid_search_cosine_failure_falls_back_to_bm25(self) -> None:
        """If cosine fails, returns BM25 results."""
        embedding_client = AsyncMock()
        embedding_client.embed_query.return_value = [0.1] * 128

        bm25_results = [
            {"user_id": "user-a", "score": 0.7, "model": "test"},
        ]

        with (
            patch("app.repositories.user_search.cosine_search",
                  side_effect=Exception("pgvector down")),
            patch("app.repositories.user_search.bm25_search", return_value=bm25_results),
        ):
            results = await hybrid_search("developer", embedding_client, top_k=10)

        assert len(results) == 1
        assert results[0]["user_id"] == "user-a"

    @pytest.mark.asyncio
    async def test_hybrid_search_both_fail_return_empty(self) -> None:
        """If both searches fail, returns empty list."""
        embedding_client = AsyncMock()
        embedding_client.embed_query.return_value = [0.1] * 128

        with (
            patch("app.repositories.user_search.cosine_search",
                  side_effect=Exception("pgvector down")),
            patch("app.repositories.user_search.bm25_search",
                  side_effect=Exception("full-text down")),
        ):
            results = await hybrid_search("developer", embedding_client, top_k=10)

        assert results == []

    @pytest.mark.asyncio
    async def test_hybrid_search_top_k_respected(self) -> None:
        """Results are limited to top_k."""
        embedding_client = AsyncMock()
        embedding_client.embed_query.return_value = [0.1] * 128

        cosine_results = [
            {"user_id": f"user-{i}", "score": 0.9 - (i * 0.1), "model": "test"}
            for i in range(10)
        ]

        with (
            patch("app.repositories.user_search.cosine_search", return_value=cosine_results),
            patch("app.repositories.user_search.bm25_search", return_value=[]),
        ):
            results = await hybrid_search("developer", embedding_client, top_k=3)

        assert len(results) == 3


class TestSearchUsersEndpoint:
    """Tests for the FastAPI /v1/users/search endpoint."""

    @pytest.mark.asyncio
    async def test_endpoint_returns_correct_shape(self) -> None:
        """Endpoint returns user_ids, relevance, and trace_id."""
        settings = _settings()
        mock_request = Mock()
        mock_request.app.state.settings = settings

        embedding_client = AsyncMock()
        embedding_client.embed_query.return_value = [0.1] * 128

        mock_results = [
            {"user_id": "user-1", "score": 0.92, "model": "test"},
            {"user_id": "user-2", "score": 0.85, "model": "test"},
        ]

        with (
            patch("app.api.v1.user_search.create_embedding_client",
                  return_value=embedding_client),
            patch("app.api.v1.user_search.hybrid_search",
                  return_value=mock_results),
        ):
            from app.api.v1.user_search import search_users
            result = await search_users(mock_request, q="developer", top_k=10)

        assert "user_ids" in result
        assert "relevance" in result
        assert "trace_id" in result
        assert result["user_ids"] == ["user-1", "user-2"]
        assert result["relevance"] == [0.92, 0.85]
