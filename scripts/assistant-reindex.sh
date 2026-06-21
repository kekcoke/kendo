#!/usr/bin/env bash
# =============================================================================
# W6 — Assistant Index Rebuild Script (M5.12)
# =============================================================================
# CLI wrapper to rebuild the W6 Document Q&A corpus index from scratch.
# Usage: ./scripts/assistant-reindex.sh
#
# Design:
# - Invokes the FastAPI internal reindex endpoint
# - Idempotent: safe to run multiple times
# - Atomic: index is swapped atomically on success
# - Debounced: respects KENDO_INDEX_DEBOUNCE_SECONDS env var (default 300)
# =============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
FASTAPI_URL="${KENDO_FASTAPI_URL:-http://localhost:8000}"
DEBOUNCE_SECONDS="${KENDO_INDEX_DEBOUNCE_SECONDS:-300}"
INDEX_LOCK="/tmp/kendo_assistant_reindex.lock"

echo "[assistant-reindex] W6 Document Q&A index rebuild"
echo "[assistant-reindex] FastAPI URL: $FASTAPI_URL"
echo "[assistant-reindex] Project root: $PROJECT_ROOT"

# Debounce check — prevent rebuilds more frequent than DEBOUNCE_SECONDS
if [ -f "$INDEX_LOCK" ]; then
    lock_mtime=$(stat -f %m "$INDEX_LOCK" 2>/dev/null || stat -c %Y "$INDEX_LOCK" 2>/dev/null)
    now=$(date +%s)
    elapsed=$((now - lock_mtime))
    if [ "$elapsed" -lt "$DEBOUNCE_SECONDS" ]; then
        remaining=$((DEBOUNCE_SECONDS - elapsed))
        echo "[assistant-reindex] Debounced — last rebuild was ${elapsed}s ago."
        echo "[assistant-reindex] Next rebuild available in ${remaining}s."
        echo "[assistant-reindex] To override: rm -f $INDEX_LOCK"
        exit 0
    fi
fi

# Touch lock file
touch "$INDEX_LOCK"

# Call FastAPI reindex endpoint (internal)
echo "[assistant-reindex] Triggering index rebuild..."
response=$(curl -s -o /dev/null -w "%{http_code}" -X POST \
    "${FASTAPI_URL}/internal/assistant/reindex" \
    -H "Content-Type: application/json" \
    -d "{\"corpus_root\": \"$PROJECT_ROOT\"}")

if [ "$response" = "200" ]; then
    echo "[assistant-reindex] Index rebuilt successfully."
else
    echo "[assistant-reindex] Failed with HTTP $response."
    rm -f "$INDEX_LOCK"
    exit 1
fi
