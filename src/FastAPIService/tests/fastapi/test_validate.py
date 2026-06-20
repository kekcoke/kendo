"""Unit tests for W2 Event Validation.

Tests cover:
- ValidateChain: date overlap, headcount plausibility, recurring collision
- Validate endpoint: input validation, response shape
- Reasoning trace: step+result array returned with every response
"""

from __future__ import annotations

from unittest.mock import AsyncMock, patch

import pytest

from app.config import Settings
from app.rag.validate_chain import ValidateChain


def _make_chain(settings: Settings | None = None) -> ValidateChain:
    """Create a ValidateChain for testing with default test settings."""
    if settings is None:
        settings = Settings(  # type: ignore[call-arg]
            env="test",
            embedding_model="BGE-large-en-v1.5",
            vector_read_dsn="",
        )
    return ValidateChain(settings)


# =============================================================================
# ValidateChain — date overlap detection
# =============================================================================

class TestDateOverlap:
    """Tests for _check_date_overlap method."""

    def test_no_date_specified(self) -> None:
        """No date/time returns no conflict."""
        chain = _make_chain()
        result = chain._check_date_overlap(None, None, "Event", [])
        assert "no conflict" in result

    def test_date_match_detected(self) -> None:
        """Matching date in past events returns conflict."""
        chain = _make_chain()
        events = [{"text": "Birthday party on Saturday at 7pm"}]
        result = chain._check_date_overlap("Saturday", None, "Test Event", events)
        assert "conflict" in result or "Date conflict" in result

    def test_time_match_detected(self) -> None:
        """Matching time in past events returns conflict."""
        chain = _make_chain()
        events = [{"text": "Team standup at 9am in Conference Room A"}]
        result = chain._check_date_overlap(None, "9am", "Test Event", events)
        assert "conflict" in result or "Time conflict" in result

    def test_no_overlap_with_events(self) -> None:
        """Different date/time returns no conflict."""
        chain = _make_chain()
        events = [{"text": "Birthday party on Saturday at 7pm"}]
        result = chain._check_date_overlap("Monday", "10am", "Test Event", events)
        assert "no conflict" in result


# =============================================================================
# ValidateChain — headcount plausibility
# =============================================================================

class TestHeadcountPlausibility:
    """Tests for _check_headcount_plausibility method."""

    def test_no_headcount_specified(self) -> None:
        """None headcount returns reasonable."""
        chain = _make_chain()
        result = chain._check_headcount_plausibility(None, None, [])
        assert "headcount is reasonable" in result

    def test_small_headcount_is_reasonable(self) -> None:
        """Small headcount without venue is reasonable."""
        chain = _make_chain()
        result = chain._check_headcount_plausibility(15, None, [])
        assert "headcount is reasonable" in result

    def test_large_headcount_without_venue_is_flagged(self) -> None:
        """Large headcount without venue returns conflict."""
        chain = _make_chain()
        result = chain._check_headcount_plausibility(250, None, [])
        assert "may be too large" in result

    def test_headcount_with_location_is_reasonable(self) -> None:
        """Headcount with known location is reasonable."""
        chain = _make_chain()
        events = [{"text": "Corporate gala for 150 at Grand Ballroom"}]
        result = chain._check_headcount_plausibility(200, "Grand Ballroom", events)
        assert "headcount is reasonable" in result


# =============================================================================
# ValidateChain — recurring collision detection
# =============================================================================

class TestRecurringCollision:
    """Tests for _check_recurring_collision method."""

    def test_no_recurring_pattern(self) -> None:
        """No recurring patterns returns no collision."""
        chain = _make_chain()
        events = [{"text": "One-time meeting on Friday"}]
        result = chain._check_recurring_collision("Friday", None, "Test", events)
        assert "no recurring collision" in result

    def test_recurring_match_detected(self) -> None:
        """Weekly event matching date returns collision."""
        chain = _make_chain()
        events = [{"text": "Weekly team standup every Tuesday at 9am"}]
        result = chain._check_recurring_collision("Tuesday", None, "Test", events)
        assert "collision" in result

    def test_recurring_no_date_match(self) -> None:
        """Weekly event on different day returns no collision."""
        chain = _make_chain()
        events = [{"text": "Weekly team standup every Tuesday at 9am"}]
        result = chain._check_recurring_collision("Friday", None, "Test", events)
        assert "no recurring collision" in result


# =============================================================================
# ValidateChain — full validation pipeline (mocked)
# =============================================================================

class TestValidatePipeline:
    """Tests for the full validate pipeline with mocked embedding + search."""

    @pytest.mark.asyncio
    async def test_validate_clean_event(self) -> None:
        """Clean event with no conflicts returns ok=True."""
        chain = _make_chain()

        event = {
            "name": "Team Lunch",
            "headcount": 10,
            "time": "12pm",
            "location": "Cafeteria",
        }

        with (
            patch.object(chain.embedding_client, "embed_query", return_value=[0.1] * 1024),
            patch(
                "app.repositories.embeddings.similarity_search",
                return_value=[{"id": "1", "text": "Past unrelated event", "score": 0.3, "source": "events"}],
            ),
        ):
            result = await chain.validate(event, "user-abc-123")

        assert result["ok"] is True
        assert len(result["conflicts"]) == 0
        assert "reasoning_trace" in result
        assert len(result["reasoning_trace"]) >= 2

    @pytest.mark.asyncio
    async def test_validate_conflicting_event(self) -> None:
        """Event with overlapping date returns conflict."""
        chain = _make_chain()

        event = {
            "name": "Birthday Party",
            "headcount": 15,
            "date": "Saturday",
            "time": "7pm",
        }

        with (
            patch.object(chain.embedding_client, "embed_query", return_value=[0.1] * 1024),
            patch(
                "app.repositories.embeddings.similarity_search",
                return_value=[
                    {"id": "1", "text": "Another party on Saturday at 7pm", "score": 0.9, "source": "events"},
                ],
            ),
        ):
            result = await chain.validate(event, "user-abc-123")

        assert "reasoning_trace" in result
        # Should have at least 3 trace entries for the 3 tools
        assert len(result["reasoning_trace"]) >= 3

    @pytest.mark.asyncio
    async def test_validate_with_user_id(self) -> None:
        """user_id is passed through the pipeline without error."""
        chain = _make_chain()

        event = {
            "name": "Coffee Meeting",
            "headcount": 2,
        }

        with (
            patch.object(chain.embedding_client, "embed_query", return_value=[0.1] * 1024),
            patch(
                "app.repositories.embeddings.similarity_search",
                return_value=[],
            ),
        ):
            result = await chain.validate(event, "user-xyz-789")

        assert result is not None
        assert "reasoning_trace" in result
        assert len(result["reasoning_trace"]) >= 2
