"""Unit tests for W5 Embeddings Backfill — reindex CLI + UserService client.

Tests cover:
- parse_args: argument parsing
- _load_checkpoint / _save_checkpoint: checkpoint I/O
- _ensure_no_concurrent_run: lock-file collision
- run_reindex: pipeline with mocked client + embedding
"""

from __future__ import annotations

from pathlib import Path
from unittest.mock import AsyncMock, Mock, patch

import pytest

from app.config import Settings
from app.jobs.reindex import (
    _ensure_no_concurrent_run,
    _load_checkpoint,
    _release_lock,
    _save_checkpoint,
    parse_args,
    run_reindex,
)
from app.integrations.user_service_client import UserServiceClient


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
# parse_args
# =============================================================================

class TestParseArgs:
    """Tests for reindex CLI argument parsing."""

    def test_requires_since(self) -> None:
        """--since is required."""
        with pytest.raises(SystemExit):
            parse_args([])

    def test_defaults(self) -> None:
        """Default batch-size and dry-run."""
        args = parse_args(["--since", "2026-01-01"])
        assert args.since == "2026-01-01"
        assert args.batch_size == 64
        assert args.dry_run is False

    def test_custom_batch_size(self) -> None:
        """--batch-size is configurable."""
        args = parse_args(["--since", "2026-01-01", "--batch-size", "32"])
        assert args.batch_size == 32

    def test_dry_run_flag(self) -> None:
        """--dry-run flag is set."""
        args = parse_args(["--since", "2026-01-01", "--dry-run"])
        assert args.dry_run is True


# =============================================================================
# Checkpoint I/O
# =============================================================================

class TestCheckpoint:
    """Tests for checkpoint load/save operations."""

    def test_load_missing(self) -> None:
        """Missing checkpoint returns None."""
        assert _load_checkpoint() is None

    def test_save_and_load(self, tmp_path: Path) -> None:
        """Saved checkpoint is loadable."""
        test_path = tmp_path / "reindex_checkpoint.json"
        with patch("app.jobs.reindex.CHECKPOINT_PATH", test_path):
            state = {"last_processed_event_id": "evt-10", "last_processed_at": "2026-01-01T00:00:00"}
            _save_checkpoint(state)
            loaded = _load_checkpoint()
            assert loaded is not None
            assert loaded["last_processed_event_id"] == "evt-10"

    def test_corrupted_checkpoint(self, tmp_path: Path) -> None:
        """Corrupted checkpoint returns None without raising."""
        test_path = tmp_path / "reindex_checkpoint.json"
        test_path.write_text("not-json")
        with patch("app.jobs.reindex.CHECKPOINT_PATH", test_path):
            assert _load_checkpoint() is None


# =============================================================================
# Concurrent run prevention
# =============================================================================

class TestConcurrentRun:
    """Tests for lock-file collision detection."""

    def test_no_lock_allows_run(self, tmp_path: Path) -> None:
        """No lock file = OK."""
        test_path = tmp_path / "reindex_checkpoint.json"
        with patch("app.jobs.reindex.CHECKPOINT_PATH", test_path):
            _ensure_no_concurrent_run()

    def test_stale_lock_allows_run(self, tmp_path: Path) -> None:
        """Lock older than 1 hour is considered stale and re-created fresh."""
        import os
        from datetime import datetime, timezone

        test_path = tmp_path / "reindex_checkpoint.json"
        lock = test_path.with_suffix(".lock")
        # Create lock with mtime > 1 hour ago (no touch to avoid resetting mtime)
        lock.write_text("stale lock")
        old_time = datetime.now(timezone.utc).timestamp() - 7200
        os.utime(lock, (old_time, old_time))

        with patch("app.jobs.reindex.CHECKPOINT_PATH", test_path):
            _ensure_no_concurrent_run()
            # Stale lock was removed and a fresh one created
            assert lock.exists()
            # Fresh lock is recent (< 60 seconds old)
            age = datetime.now(timezone.utc).timestamp() - lock.stat().st_mtime
            assert age < 60

    def test_active_lock_raises(self, tmp_path: Path) -> None:
        """Recent lock file raises SystemExit."""
        test_path = tmp_path / "reindex_checkpoint.json"
        with patch("app.jobs.reindex.CHECKPOINT_PATH", test_path):
            lock = test_path.with_suffix(".lock")
            lock.touch()
            with pytest.raises(SystemExit):
                _ensure_no_concurrent_run()

    def test_release_lock_removes(self, tmp_path: Path) -> None:
        """_release_lock removes the lock file."""
        test_path = tmp_path / "reindex_checkpoint.json"
        with patch("app.jobs.reindex.CHECKPOINT_PATH", test_path):
            lock = test_path.with_suffix(".lock")
            lock.touch()
            _release_lock()
            assert not lock.exists()


# =============================================================================
# run_reindex
# =============================================================================

class TestRunReindex:
    """Tests for the reindex pipeline with mocked services."""

    @pytest.mark.asyncio
    async def test_empty_events_no_work(self) -> None:
        """No events returned = no processing."""
        settings = _settings()
        with (
            patch("app.jobs.reindex.UserServiceClient") as mock_cls,
            patch("app.jobs.reindex.create_embedding_client"),
            patch("app.jobs.reindex._save_checkpoint"),
        ):
            mock_client = AsyncMock(spec=UserServiceClient)
            mock_client.get_events.return_value = []
            mock_cls.return_value = mock_client

            await run_reindex(settings, since="2026-01-01", batch_size=64)
            # Should complete without error

    @pytest.mark.asyncio
    async def test_dry_run_no_writes(self) -> None:
        """Dry run does not call write_embeddings_batch."""
        settings = _settings()
        events = [{"id": "evt-1", "name": "Test Event", "description": "A test"}]

        with (
            patch("app.jobs.reindex.UserServiceClient") as mock_cls,
            patch("app.jobs.reindex.create_embedding_client") as mock_embed_factory,
            patch("app.jobs.reindex._save_checkpoint"),
        ):
            mock_client = AsyncMock(spec=UserServiceClient)
            mock_client.get_events.return_value = events
            mock_cls.return_value = mock_client

            mock_embed = AsyncMock()
            mock_embed.embed_documents.return_value = [[0.1] * 128]
            mock_embed_factory.return_value = mock_embed

            await run_reindex(settings, since="2026-01-01", batch_size=64, dry_run=True)

            mock_client.write_embeddings_batch.assert_not_called()

    @pytest.mark.asyncio
    async def test_real_run_writes_embeddings(self) -> None:
        """Normal run calls write_embeddings_batch."""
        settings = _settings()
        events = [{"id": "evt-1", "name": "Test Event", "description": "A test"}]

        with (
            patch("app.jobs.reindex.UserServiceClient") as mock_cls,
            patch("app.jobs.reindex.create_embedding_client") as mock_embed_factory,
            patch("app.jobs.reindex._save_checkpoint"),
        ):
            mock_client = AsyncMock(spec=UserServiceClient)
            # Return events first call, empty on subsequent calls (pagination ends)
            mock_client.get_events.side_effect = [events, []]
            mock_client.write_embeddings_batch.return_value = {
                "job_id": "job-1", "processed": 1, "status": "accepted",
            }
            mock_cls.return_value = mock_client

            mock_embed = AsyncMock()
            mock_embed.embed_documents.return_value = [[0.1] * 128]
            mock_embed_factory.return_value = mock_embed

            await run_reindex(settings, since="2026-01-01", batch_size=64)

            mock_client.write_embeddings_batch.assert_called_once()
            args, _ = mock_client.write_embeddings_batch.call_args
            assert len(args[0]) == 1
            assert args[0][0]["target"] == "event"
            assert args[0][0]["targetId"] == "evt-1"
