"""Unit tests for W5 apscheduler cron job.

Tests cover:
- create_scheduler: scheduler creation with default and custom cron
- scheduled_reindex: job execution with mocked dependencies
"""

from __future__ import annotations

from datetime import datetime, timezone
from unittest.mock import AsyncMock, Mock, patch

import pytest

from app.config import Settings
from app.jobs.scheduler import create_scheduler, scheduled_reindex


# =============================================================================
# Helpers
# =============================================================================

def _settings(**overrides: str | int | bool) -> Settings:
    """Create test settings with defaults."""
    kwargs: dict = {
        "env": "test",
        "embedding_model": "test-model",
        "embedding_dimension": 128,
        "user_service_base_url": "http://test:5001",
        "user_service_timeout": 5,
    }
    kwargs.update(overrides)
    return Settings(**kwargs)  # type: ignore[call-arg]


# =============================================================================
# create_scheduler
# =============================================================================

class TestCreateScheduler:
    """Tests for scheduler creation."""

    def test_scheduler_created_with_default_cron(self) -> None:
        """Default cron creates scheduler with daily 02:00 UTC trigger."""
        settings = _settings()
        scheduler = create_scheduler(settings)
        assert scheduler.running is False
        jobs = scheduler.get_jobs()
        assert len(jobs) == 1
        assert jobs[0].id == "w5_reindex"
        assert jobs[0].name == "W5 Embeddings Backfill & Re-indexing"

    def test_scheduler_job_has_correct_trigger(self) -> None:
        """Custom cron override is reflected in the job trigger."""
        settings = _settings()
        with patch.dict("os.environ", {"FASTAPI__REINDEX__SCHEDULE": "0 3 * * *"}):
            scheduler = create_scheduler(settings)
            job = scheduler.get_job("w5_reindex")
            assert job is not None
            # The trigger repr contains the cron expression
            trigger_repr = repr(job.trigger)
            # Check that the trigger reflects hour=3, minute=0
            assert "0 3 * * *" in trigger_repr or "hour='3'" in trigger_repr


# =============================================================================
# scheduled_reindex
# =============================================================================

class TestScheduledReindex:
    """Tests for the scheduled reindex job execution."""

    @pytest.mark.asyncio
    async def test_scheduled_reindex_with_checkpoint(self) -> None:
        """Scheduled job loads checkpoint and resumes."""
        settings = _settings()

        with (
            patch("app.jobs.scheduler.acquire_lock", return_value=True),
            patch("app.jobs.scheduler.release_lock"),
            patch("app.jobs.scheduler.load_checkpoint") as mock_load_cp,
            patch("app.jobs.scheduler.run_reindex") as mock_run,
        ):
            mock_load_cp.return_value = {
                "last_processed_event_id": "evt-50",
                "last_processed_at": "2026-06-15T00:00:00",
                "model_version": "test-model",
            }

            await scheduled_reindex(settings)

            mock_run.assert_called_once()
            # Should resume with the timestamp from checkpoint
            assert mock_run.call_args[1]["since"] == "2026-06-15T00:00:00"

    @pytest.mark.asyncio
    async def test_scheduled_reindex_no_checkpoint(self) -> None:
        """No checkpoint starts fresh from default date."""
        settings = _settings()

        with (
            patch("app.jobs.scheduler.acquire_lock", return_value=True),
            patch("app.jobs.scheduler.release_lock"),
            patch("app.jobs.scheduler.load_checkpoint", return_value=None),
            patch("app.jobs.scheduler.run_reindex") as mock_run,
        ):
            await scheduled_reindex(settings)

            mock_run.assert_called_once()
            assert mock_run.call_args[1]["since"] == "2026-01-01T00:00:00"

    @pytest.mark.asyncio
    async def test_scheduled_reindex_lock_failure(self) -> None:
        """Lock failure skips the job."""
        settings = _settings()

        with (
            patch("app.jobs.scheduler.acquire_lock", return_value=False),
            patch("app.jobs.scheduler.release_lock"),
            patch("app.jobs.scheduler.run_reindex") as mock_run,
        ):
            await scheduled_reindex(settings)

            mock_run.assert_not_called()

    @pytest.mark.asyncio
    async def test_scheduled_reindex_releases_lock_on_error(self) -> None:
        """Lock is released even if reindex raises."""
        settings = _settings()

        with (
            patch("app.jobs.scheduler.acquire_lock", return_value=True),
            patch("app.jobs.scheduler.release_lock") as mock_release,
            patch("app.jobs.scheduler.load_checkpoint", return_value=None),
            patch("app.jobs.scheduler.run_reindex", side_effect=RuntimeError("fail")),
        ):
            with pytest.raises(RuntimeError):
                await scheduled_reindex(settings)

            mock_release.assert_called_once()
