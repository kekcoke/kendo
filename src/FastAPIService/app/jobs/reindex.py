"""W5 Embeddings Backfill CLI — reindex events into embedding vectors.

Usage:
    python -m app.jobs.reindex --since 2026-01-01 --batch-size 64 --dry-run
    python -m app.jobs.reindex --since 2026-01-01 --batch-size 64

Resumable: reads checkpoint from last_processed_event_id to avoid re-processing.
"""

from __future__ import annotations

import argparse
import json
import logging
import os
import sys
from datetime import datetime, timezone
from pathlib import Path

from app.config import Settings
from app.integrations.user_service_client import UserServiceClient
from app.rag.embeddings_client import create_embedding_client

logger = logging.getLogger(__name__)

CHECKPOINT_PATH = Path("/var/kendo/reindex_checkpoint.json")


def _load_checkpoint() -> dict | None:
    """Load checkpoint file, returns None if missing or invalid."""
    try:
        if CHECKPOINT_PATH.exists():
            with open(CHECKPOINT_PATH) as f:
                return json.load(f)
    except (json.JSONDecodeError, OSError) as exc:
        logger.warning("Failed to read checkpoint: %s", exc)
    return None


def _save_checkpoint(state: dict) -> None:
    """Write checkpoint atomically via tempfile + rename."""
    tmp = CHECKPOINT_PATH.with_suffix(".tmp")
    CHECKPOINT_PATH.parent.mkdir(parents=True, exist_ok=True)
    with open(tmp, "w") as f:
        json.dump(state, f)
    tmp.rename(CHECKPOINT_PATH)


def _ensure_no_concurrent_run() -> None:
    """Fail fast if another reindex process is running."""
    lock = CHECKPOINT_PATH.with_suffix(".lock")
    if lock.exists():
        # Check if lock is stale (> 1 hour)
        age = datetime.now(timezone.utc).timestamp() - lock.stat().st_mtime
        if age < 3600:
            logger.error("Concurrent reindex detected at %s", lock)
            sys.exit(1)
        else:
            logger.warning("Removing stale lock file (%ds old)", int(age))
            lock.unlink(missing_ok=True)
    lock.touch()


def _release_lock() -> None:
    lock = CHECKPOINT_PATH.with_suffix(".lock")
    lock.unlink(missing_ok=True)


async def run_reindex(
    settings: Settings,
    since: str,
    batch_size: int = 64,
    dry_run: bool = False,
    model_version: str | None = None,
) -> None:
    """Reindex events from UserService, computing and writing embeddings.

    Args:
        settings: Application settings.
        since: ISO timestamp to start reindex from.
        batch_size: Number of events to process per batch.
        dry_run: If True, only log what would be done — no writes.
        model_version: Embedding model version string. If None, uses settings.embedding_model.
    """
    client = UserServiceClient(
        base_url=settings.user_service_base_url,
        timeout_seconds=settings.user_service_timeout,
    )
    embedding_client = create_embedding_client(settings)
    effective_model = model_version or settings.embedding_model

    # Load checkpoint for resume
    checkpoint = _load_checkpoint()
    if checkpoint and checkpoint.get("last_processed_event_id"):
        logger.info(
            "Resuming from checkpoint — last processed event ID: %s",
            checkpoint["last_processed_event_id"],
        )
        resume_id = checkpoint["last_processed_event_id"]
    else:
        resume_id = None

    page_since = since
    total_processed = 0

    while True:
        # Fetch a page of events from UserService
        events = await client.get_events(since=page_since, limit=batch_size)

        if not events:
            logger.info("No more events to process. Done.")
            break

        # Skip events already processed (checkpoint resume)
        if resume_id:
            events = [e for e in events if str(e.get("id", "")) > resume_id]
            if not events:
                break

        # Compute embeddings for this batch
        texts = []
        for event in events:
            name = event.get("name", "")
            desc = event.get("description", "")
            text = f"{name}: {desc}" if desc else name
            texts.append(text)

        try:
            embeddings = await embedding_client.embed_documents(texts)
        except Exception as exc:
            logger.error("Embedding computation failed for batch: %s", exc)
            raise

        if dry_run:
            logger.info(
                "DRY RUN: would process %d events, write %d embeddings",
                len(events),
                len(embeddings),
            )
        else:
            # Write embeddings through UserService admin endpoint
            batch_payload = []
            for event, embedding in zip(events, embeddings):
                batch_payload.append({
                    "target": "event",
                    "targetId": event["id"],
                    "modelName": effective_model,
                    "dimensions": settings.embedding_dimension,
                    "embedding": embedding,
                })

            await client.write_embeddings_batch(batch_payload)

        total_processed += len(events)

        # Update checkpoint
        last_id = str(events[-1]["id"])
        _save_checkpoint({
            "last_processed_event_id": last_id,
            "last_processed_at": datetime.now(timezone.utc).isoformat(),
            "model_version": effective_model,
        })
        resume_id = None  # After first save, rely on checkpoint

        # Move to next page
        page_since = events[-1].get("updated_at", page_since)

        # Safety: batch timeout check
        if total_processed >= 10000:
            logger.info("Reached 10k event limit. Exiting.")
            break

    logger.info(
        "Reindex complete. Processed %d events. %s",
        total_processed,
        "(dry run — no writes)" if dry_run else "embeddings written.",
    )


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    """Parse command-line arguments."""
    parser = argparse.ArgumentParser(
        description="W5 Embeddings Backfill — reindex events into embedding vectors.",
    )
    parser.add_argument(
        "--since",
        required=True,
        help="ISO timestamp to start from (e.g., 2026-01-01T00:00:00)",
    )
    parser.add_argument(
        "--batch-size",
        type=int,
        default=64,
        help="Events per batch (default: 64)",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Log only, do not write embeddings",
    )
    return parser.parse_args(argv)


async def main() -> None:
    """CLI entry point."""
    args = parse_args()
    settings = Settings()  # type: ignore[call-arg]

    logging.basicConfig(
        level=getattr(logging, settings.log_level.upper(), logging.INFO),
        format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
    )

    logger.info(
        "Starting reindex since=%s batch_size=%d dry_run=%s",
        args.since, args.batch_size, args.dry_run,
    )

    _ensure_no_concurrent_run()
    try:
        await run_reindex(
            settings=settings,
            since=args.since,
            batch_size=args.batch_size,
            dry_run=args.dry_run,
        )
    finally:
        _release_lock()


if __name__ == "__main__":
    import asyncio
    asyncio.run(main())
