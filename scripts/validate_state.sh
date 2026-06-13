#!/usr/bin/env bash
# validate_state.sh — Phase 5 guard
# Usage: ./scripts/validate_state.sh <DAY_NUMBER>
# Exits 0 if all expected artifacts for the given day exist and current_state.md is consistent.
# Exits 1 with a descriptive error on any failure.

set -euo pipefail

DAY="${1:?Usage: validate_state.sh <DAY_NUMBER>}"
DAY_PAD=$(printf '%02d' "$DAY")
STATE=".ai/current_state.md"
ERRORS=()

# ── Helper ────────────────────────────────────────────────────────────────────
check_file() {
  local path="$1"
  local label="$2"
  if [[ ! -f "$path" ]]; then
    ERRORS+=("MISSING $label: $path")
  fi
}

check_grep() {
  local file="$1"
  local pattern="$2"
  local label="$3"
  if ! grep -q "$pattern" "$file" 2>/dev/null; then
    ERRORS+=("NOT FOUND in $file — expected: $label")
  fi
}

# ── 1. current_state.md exists ────────────────────────────────────────────────
check_file "$STATE" "current_state.md"

# ── 2. Phase artifacts for this day ──────────────────────────────────────────
check_file "docs/architecture/day_${DAY_PAD}_spec.md"           "Phase 1: Architecture Spec"
check_file "docs/architecture/day_${DAY_PAD}_review_report.md"  "Phase 4b: Review Report"
check_file "ops/runbooks/day_${DAY_PAD}_runbook.md"             "Phase 4: Ops Runbook"

# ── 3. current_state.md structure checks ─────────────────────────────────────
check_grep "$STATE" "current_phase: 0"        "current_phase reset to 0"
check_grep "$STATE" "incomplete_tasks"         "## Incomplete Tasks section"
check_grep "$STATE" "## Completed Days"        "## Completed Days section"
check_grep "$STATE" "## Carry-Forward Items"   "## Carry-Forward Items section"
check_grep "$STATE" "## Architectural Decisions Log" "## Architectural Decisions Log section"

# ── 4. Completed Days entry for this day ─────────────────────────────────────
if ! grep -q "| ${DAY} " "$STATE" 2>/dev/null && ! grep -q "| 0${DAY} " "$STATE" 2>/dev/null; then
  ERRORS+=("No Completed Days row found for Day ${DAY} in $STATE")
fi

# ── 5. Changelog entry ────────────────────────────────────────────────────────
CHANGELOG_COUNT=$(find changelog/ -name "*.md" 2>/dev/null | wc -l | tr -d ' ')
if [[ "$CHANGELOG_COUNT" -eq 0 ]]; then
  ERRORS+=("No changelog/*.md files found — Phase 5 changelog write missing")
fi

# ── 6. .env not committed ────────────────────────────────────────────────────
if git ls-files --error-unmatch .env > /dev/null 2>&1; then
  ERRORS+=("SECURITY: .env is tracked by git — add to .gitignore and remove from index")
fi

# ── Report ────────────────────────────────────────────────────────────────────
echo ""
echo "━━━ validate_state.sh — Day ${DAY_PAD} ━━━"
if [[ ${#ERRORS[@]} -eq 0 ]]; then
  echo "✅ All checks passed. Day ${DAY_PAD} state is valid."
  echo ""
  exit 0
else
  echo "❌ ${#ERRORS[@]} check(s) failed:"
  for err in "${ERRORS[@]}"; do
    echo "   • $err"
  done
  echo ""
  exit 1
fi
