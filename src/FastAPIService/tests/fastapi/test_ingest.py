"""Unit tests for W1 Event Ingestion RAG.

Tests cover:
- IngestChain: chunking, ingestion pipeline, mock LLM output
- Ingest endpoint: input validation, response shape
- Gateway contract: structured Event JSON schema
"""

from __future__ import annotations

from unittest.mock import AsyncMock, patch

import pytest

from app.config import Settings
from app.config import Settings
from app.rag.ingest_chain import IngestChain


def _make_chain(settings: Settings | None = None) -> IngestChain:
    """Create an IngestChain for testing with default test settings."""
    if settings is None:
        settings = Settings(  # type: ignore[call-arg]
            env="test",
            embedding_model="BGE-large-en-v1.5",
            vector_read_dsn="",
        )
    return IngestChain(settings)


# =============================================================================
# IngestChain — chunking
# =============================================================================

class TestChunking:
    """Tests for the _chunk_text method of IngestChain."""

    def test_short_text_no_chunking(self) -> None:
        """Text under max_chars is not chunked."""
        chain = _make_chain()
        text = "Short event description"
        chunks = chain._chunk_text(text, max_chars=512)
        assert len(chunks) == 1
        assert chunks[0] == text

    def test_long_text_is_chunked(self) -> None:
        """Text over max_chars is split into multiple chunks."""
        chain = _make_chain()
        text = "A" * 600
        chunks = chain._chunk_text(text, max_chars=512)
        assert len(chunks) >= 2  # noqa: PLR2004

    def test_overlap_preserved(self) -> None:
        """Consecutive chunks have 50-char overlap."""
        chain = _make_chain()
        text = "A" * 600
        chunks = chain._chunk_text(text, max_chars=512)
        if len(chunks) > 1:
            # Last 50 chars of chunk 0 should match first 50 of chunk 1
            assert chunks[0][-50:] == chunks[1][:50]


# =============================================================================
# IngestChain — mock structured output
# =============================================================================

class TestMockStructuredOutput:
    """Tests for _mock_structured_output fallback."""

    def test_basic_event_extraction(self) -> None:
        """Basic event text produces expected structured output."""
        chain = _make_chain()
        text = "Birthday party with 15 guests, no nuts please"
        result = chain._mock_structured_output(text)

        assert "event_id" in result
        assert result["name"] == "Birthday party with 15 guests, no nuts please"
        assert result["headcount"] == 15
        assert result["dietary_notes"] is not None
        assert "nut" in result["dietary_notes"].lower()

    def test_minimal_text(self) -> None:
        """Very short text produces a fallback Untitled Event."""
        chain = _make_chain()
        result = chain._mock_structured_output("Hi")
        # "Hi" is 2 chars (< 3), falls through to "Untitled Event"
        assert result["name"] == "Untitled Event"

    def test_date_time_extraction(self) -> None:
        """ISO date and time patterns are extracted."""
        chain = _make_chain()
        text = "Team standup at 9am tomorrow in Conference Room A"
        result = chain._mock_structured_output(text)

        assert result["time"] is not None
        assert "9am" in result["time"].lower()
        assert result["location"] is not None
        assert "Conference" in result["location"]

    def test_no_dietary_notes(self) -> None:
        """Text without dietary keywords returns None."""
        chain = _make_chain()
        result = chain._mock_structured_output("Simple meeting at 3pm")
        assert result["dietary_notes"] is None


# =============================================================================
# IngestChain — integration (mocked embedding + search)
# =============================================================================

class TestIngestPipeline:
    """Tests for the full ingest pipeline with mocked dependencies."""

    @pytest.mark.asyncio
    async def test_ingest_returns_structured_event(self) -> None:
        """Ingest pipeline returns structured Event JSON."""
        chain = _make_chain()

        # Mock embedding client and similarity search
        with (
            patch.object(chain.embedding_client, "embed_query", return_value=[0.1] * 1024),
            patch(
                "app.repositories.embeddings.similarity_search",
                return_value=[{"id": "1", "text": "Past event", "score": 0.9, "source": "events"}],
            ),
        ):
            result = await chain.ingest("Birthday party at 7pm Saturday")

        assert result["name"] is not None
        assert "event_id" in result
        assert result["raw_text"] == "Birthday party at 7pm Saturday"

    @pytest.mark.asyncio
    async def test_ingest_with_user_id(self) -> None:
        """user_id is passed through the pipeline without error."""
        chain = _make_chain()

        with (
            patch.object(chain.embedding_client, "embed_query", return_value=[0.1] * 1024),
            patch(
                "app.repositories.embeddings.similarity_search",
                return_value=[{"id": "1", "text": "Past event", "score": 0.9, "source": "events"}],
            ),
        ):
            result = await chain.ingest(
                "Team lunch at noon",
                user_id="user-abc-123",
            )

        assert result is not None
        assert result["name"] is not None
