"""Checkpoint persistence for W5 reindex job.

Thread-safe and process-safe via file-lock. Stores the last processed event
ID and model version so the job can resume after interruption.
"""

from __future__ import annotations

import json
import logging
import os
from datetime import datetime, timezone
from pathlib import Path

logger = logging.getLogger(__name__)

# Default checkpoint path — configurable via env var
_CHECKPOINT_PATH = Path(os.environ.get("FASTAPI__REINDEX__CHECKPOINT_PATH", "/var/kendo/reindex_checkpoint.json"))


def set_checkpoint_path(path: str | Path) -> None:
    """Override the checkpoint path (for testing)."""
    global _CHECKPOINT_PATH
    _CHECKPOINT_PATH = Path(path)


def load_checkpoint() -> dict | None:
    """Load checkpoint file, returns None if missing or invalid."""
    try:
        if _CHECKPOINT_PATH.exists():
            with open(_CHECKPOINT_PATH) as f:
                data = json.load(f)
                if "last_processed_event_id" in data:
                    return data
    except (json.JSONDecodeError, OSError) as exc:
        logger.warning("Failed to read checkpoint: %s", exc)
    return None


def save_checkpoint(state: dict) -> None:
    """Write checkpoint atomically via tempfile + rename."""
    tmp = _CHECKPOINT_PATH.with_suffix(".tmp")
    _CHECKPOINT_PATH.parent.mkdir(parents=True, exist_ok=True)
    with open(tmp, "w") as f:
        json.dump(state, f)
    tmp.rename(_CHECKPOINT_PATH)
    logger.debug("Checkpoint saved: %s", state)


def acquire_lock() -> bool:
    """Acquire exclusive lock file. Returns True if acquired."""
    lock = _CHECKPOINT_PATH.with_suffix(".lock")
    if lock.exists():
        age = datetime.now(timezone.utc).timestamp() - lock.stat().st_mtime
        if age < 3600:
            logger.error("Concurrent reindex detected at %s", lock)
            return False
        else:
            logger.warning("Removing stale lock file (%ds old)", int(age))
            lock.unlink(missing_ok=True)
    lock.touch()
    return True


def release_lock() -> None:
    """Release the lock file."""
    lock = _CHECKPOINT_PATH.with_suffix(".lock")
    lock.unlink(missing_ok=True)
