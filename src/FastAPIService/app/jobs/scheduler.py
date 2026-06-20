"""apscheduler cron for periodic embeddings reindex (W5).

Runs daily at 02:00 UTC by default. Configurable via FASTAPI__REINDEX__SCHEDULE
env var in cron format. Uses checkpoint/resume for idempotent execution.
"""

from __future__ import annotations

import asyncio
import logging
from datetime import datetime, timezone

from apscheduler.schedulers.asyncio import AsyncIOScheduler
from apscheduler.triggers.cron import CronTrigger

from app.config import Settings
from app.jobs.checkpoint import acquire_lock, load_checkpoint, release_lock, save_checkpoint
from app.jobs.reindex import run_reindex

logger = logging.getLogger(__name__)

# Default: daily at 02:00 UTC
_DEFAULT_CRON = "0 2 * * *"


def _get_schedule_expression(settings: Settings) -> str:
    """Get the cron expression from settings or default."""
    # Use env var directly since Settings may not have a dedicated field
    import os
    return os.environ.get("FASTAPI__REINDEX__SCHEDULE", _DEFAULT_CRON)


async def scheduled_reindex(settings: Settings | None = None) -> None:
    """Run the reindex job as a scheduled task.

    Loads checkpoint to determine where to resume from. If no checkpoint,
    reindexes all events since the configured start date.

    Args:
        settings: Application settings. Created fresh if None.
    """
    if settings is None:
        settings = Settings()  # type: ignore[call-arg]

    logger.info("Scheduled reindex starting at %s", datetime.now(timezone.utc).isoformat())

    if not acquire_lock():
        logger.warning("Skipping scheduled reindex — another run is in progress.")
        return

    try:
        # Load checkpoint to determine resume point
        checkpoint = load_checkpoint()
        if checkpoint and checkpoint.get("last_processed_event_id"):
            since = checkpoint.get("last_processed_at", "2026-01-01T00:00:00")
            logger.info("Resuming reindex from %s (event %s)", since, checkpoint["last_processed_event_id"])
        else:
            since = "2026-01-01T00:00:00"
            logger.info("Starting fresh reindex from %s", since)

        await run_reindex(
            settings=settings,
            since=since,
            batch_size=64,
            dry_run=False,
        )
    except Exception as exc:
        logger.error("Scheduled reindex failed: %s", exc)
        raise
    finally:
        release_lock()

    logger.info("Scheduled reindex completed at %s", datetime.now(timezone.utc).isoformat())


def create_scheduler(settings: Settings | None = None) -> AsyncIOScheduler:
    """Create and configure the apscheduler instance.

    Args:
        settings: Application settings.

    Returns:
        Configured AsyncIOScheduler (not yet started).
    """
    if settings is None:
        settings = Settings()  # type: ignore[call-arg]

    cron_expr = _get_schedule_expression(settings)
    scheduler = AsyncIOScheduler(timezone="UTC")

    # Parse cron expression into trigger
    parts = cron_expr.strip().split()
    if len(parts) == 5:
        trigger = CronTrigger(
            minute=parts[0],
            hour=parts[1],
            day=parts[2],
            month=parts[3],
            day_of_week=parts[4],
            timezone="UTC",
        )
    else:
        logger.warning("Invalid cron expression '%s', using default daily at 02:00 UTC", cron_expr)
        trigger = CronTrigger(hour=2, minute=0, timezone="UTC")

    scheduler.add_job(
        scheduled_reindex,
        trigger=trigger,
        id="w5_reindex",
        name="W5 Embeddings Backfill & Re-indexing",
        replace_existing=True,
        kwargs={"settings": settings},
    )

    logger.info("Reindex scheduler configured with cron: %s", cron_expr)
    return scheduler
