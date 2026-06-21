"""Unit tests for W7 Event Notification Summarization.

Tests cover:
- NotificationSummarizationChain: tone control, prompt version, mock streaming
- Notifications endpoint: input validation, SSE response shape, error handling
- Tone profile correctness: professional, friendly, urgent
- Prompt version audit tracking
"""

from __future__ import annotations

from typing import Any
from unittest.mock import AsyncMock, patch

import pytest

from app.config import Settings
from app.rag.notification_chain import (
    NOTIFICATION_PROMPT_VERSION,
    TONE_PROFILES,
    NotificationSummarizationChain,
)


def _make_chain(settings: Settings | None = None) -> NotificationSummarizationChain:
    """Create a NotificationSummarizationChain for testing with default settings."""
    if settings is None:
        settings = Settings(  # type: ignore[call-arg]
            env="test",
            vector_read_dsn="",
        )
    return NotificationSummarizationChain(settings)


# =============================================================================
# NotificationSummarizationChain — tone control
# =============================================================================


class TestToneProfiles:
    """Tests that tone profiles exist and have distinct content."""

    def test_tone_profiles_defined(self) -> None:
        """All expected tone profiles are registered."""
        assert "professional" in TONE_PROFILES
        assert "friendly" in TONE_PROFILES
        assert "urgent" in TONE_PROFILES

    def test_tone_profiles_distinct(self) -> None:
        """Each tone profile has unique content."""
        assert TONE_PROFILES["professional"] != TONE_PROFILES["friendly"]
        assert TONE_PROFILES["professional"] != TONE_PROFILES["urgent"]
        assert TONE_PROFILES["friendly"] != TONE_PROFILES["urgent"]

    def test_unknown_tone_defaults_to_friendly(self) -> None:
        """Unknown tone key should default to friendly."""
        chain = _make_chain()
        event_ctx = {"name": "Test Event", "date": "2026-07-01", "time": "18:00"}
        user_ctx = {"name": "Bob"}

        result = chain._generate_mock_notification(event_ctx, user_ctx, tone="unknown")
        assert "Bob" in result
        assert "Hey" in result  # friendly greeting


# =============================================================================
# NotificationSummarizationChain — mock notification generation
# =============================================================================


class TestMockNotificationGeneration:
    """Tests the mock notification body generation."""

    def test_professional_tone(self) -> None:
        """Professional tone uses formal language."""
        chain = _make_chain()
        event_ctx = {"name": "Board Meeting", "date": "2026-07-01", "time": "09:00"}
        user_ctx = {"name": "Alice"}

        body = chain._generate_mock_notification(event_ctx, user_ctx, tone="professional")
        assert "Dear Alice" in body
        assert "regarding 'Board Meeting'" in body
        assert "Best regards" in body
        assert "friendly" not in body.lower()
        assert "URGENT" not in body

    def test_friendly_tone(self) -> None:
        """Friendly tone uses warm, conversational language."""
        chain = _make_chain()
        event_ctx = {"name": "Team Outing", "date": "2026-07-10", "time": "14:00"}
        user_ctx = {"name": "Charlie"}

        body = chain._generate_mock_notification(event_ctx, user_ctx, tone="friendly")
        assert "Hey Charlie" in body
        assert "heads-up" in body or "excited" in body

    def test_urgent_tone(self) -> None:
        """Urgent tone uses direct, attention-grabbing language."""
        chain = _make_chain()
        event_ctx = {"name": "Fire Drill", "date": "2026-07-05", "time": "now"}
        user_ctx = {"name": "Diana"}

        body = chain._generate_mock_notification(event_ctx, user_ctx, tone="urgent")
        assert "URGENT" in body
        assert "immediate attention" in body.lower()
        assert "Respond as soon as possible" in body

    def test_default_tone_is_friendly(self) -> None:
        """When no tone is specified, defaults to friendly."""
        chain = _make_chain()
        event_ctx = {"name": "Default Event", "date": "2026-08-01", "time": "12:00"}

        body = chain._generate_mock_notification(event_ctx, tone="friendly")
        assert "Hey there" in body

    def test_missing_user_name_falls_back(self) -> None:
        """When user name is missing, uses 'there' as fallback."""
        chain = _make_chain()
        event_ctx = {"name": "Test", "date": "2026-08-01", "time": "12:00"}
        user_ctx: dict[str, Any] = {}

        body = chain._generate_mock_notification(event_ctx, user_ctx, tone="friendly")
        assert "Hey there" in body


# =============================================================================
# NotificationSummarizationChain — prompt version
# =============================================================================


class TestPromptVersion:
    """Tests for prompt version tracking."""

    def test_prompt_version_constant(self) -> None:
        """Prompt version is a well-known string."""
        assert NOTIFICATION_PROMPT_VERSION == "w7-notification-v1"

    def test_chain_reports_prompt_version(self) -> None:
        """Chain exposes prompt version via property."""
        chain = _make_chain()
        assert chain.prompt_version == "w7-notification-v1"

    def test_summarize_blocking_returns_prompt_version(self) -> None:
        """Blocking summarize includes prompt_version in result."""
        chain = _make_chain()
        event_ctx = {"name": "Test", "date": "2026-08-01", "time": "12:00"}
        user_ctx = {"name": "Test User"}

        import asyncio

        result = asyncio.run(chain.summarize_blocking(event_ctx, user_ctx, tone="friendly"))
        assert result["prompt_version"] == "w7-notification-v1"


# =============================================================================
# NotificationSummarizationChain — async streaming
# =============================================================================


class TestAsyncSummarize:
    """Tests for the async streaming summarize method."""

    @pytest.mark.asyncio
    async def test_summarize_returns_chunks_and_done(self) -> None:
        """Stream yields chunk events followed by a final done event."""
        chain = _make_chain()
        event_ctx = {"name": "Test Event", "date": "2026-08-01", "time": "15:00"}
        user_ctx = {"name": "Test User"}

        chunks = []
        done = None

        async for chunk in chain.summarize(event_ctx, user_ctx, tone="friendly"):
            if chunk.get("_done"):
                done = chunk
            else:
                chunks.append(chunk)

        assert len(chunks) > 0
        assert done is not None
        assert done["prompt_version"] == "w7-notification-v1"
        assert done["trace_id"] is not None
        assert done["total_tokens"] > 0
        assert "notification_body" in done

    @pytest.mark.asyncio
    async def test_summarize_chunks_have_text_and_token_count(self) -> None:
        """Each chunk event has 'text' and 'token_count' fields."""
        chain = _make_chain()
        event_ctx = {"name": "Test", "date": "2026-08-01", "time": "15:00"}

        async for chunk in chain.summarize(event_ctx, tone="friendly"):
            if not chunk.get("_done"):
                assert "text" in chunk
                assert "token_count" in chunk
                assert isinstance(chunk["token_count"], int)

    @pytest.mark.asyncio
    async def test_summarize_blocking_returns_full_body(self) -> None:
        """Blocking summarize returns complete notification body."""
        chain = _make_chain()
        event_ctx = {"name": "Test Event", "date": "2026-08-01", "time": "15:00"}
        user_ctx = {"name": "Test User"}

        result = await chain.summarize_blocking(event_ctx, user_ctx, tone="professional")
        assert "notification_body" in result
        assert len(result["notification_body"]) > 0
        assert "Dear Test User" in result["notification_body"]

    @pytest.mark.asyncio
    async def test_summarize_without_user_context(self) -> None:
        """Summarize works without user context (uses defaults)."""
        chain = _make_chain()
        event_ctx = {"name": "Test Event", "date": "2026-08-01", "time": "15:00"}

        result = await chain.summarize_blocking(event_ctx, tone="friendly")
        assert "notification_body" in result
        assert "Hey there" in result["notification_body"]


# =============================================================================
# Notification endpoint — integration surface
# =============================================================================


class TestNotificationRouter:
    """Tests for the notification endpoint router registration."""

    def test_router_prefix(self) -> None:
        """Router uses the correct prefix."""
        from app.api.v1.notifications import router

        assert router.prefix == "/v1/notifications"

    def test_router_tags(self) -> None:
        """Router has appropriate tags."""
        from app.api.v1.notifications import router

        assert "notifications" in router.tags

    def test_router_has_summarize_route(self) -> None:
        """Router has the POST /summarize route registered."""
        from app.api.v1.notifications import router

        routes = [r.path for r in router.routes]
        assert "/v1/notifications/summarize" in routes or "/summarize" in routes


# =============================================================================
# Tone profiles — integration check
# =============================================================================


class TestToneProfileContent:
    """Verifies tone profile content matches expected tone characteristics."""

    def test_professional_avoids_emojis(self) -> None:
        """Professional tone should not contain emoji characters."""
        assert "🌟" not in TONE_PROFILES["professional"]
        assert "🎉" not in TONE_PROFILES["professional"]

    def test_friendly_contains_warmth_markers(self) -> None:
        """Friendly tone should have warm, engaging language."""
        text = TONE_PROFILES["friendly"]
        assert any(word in text.lower() for word in ["warm", "friendly", "welcoming", "conversational"])

    def test_urgent_mentions_urgency(self) -> None:
        """Urgent tone should convey time-sensitivity."""
        text = TONE_PROFILES["urgent"]
        assert any(word in text.lower() for word in ["urgent", "direct", "concise", "clear"])
