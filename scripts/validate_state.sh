#!/usr/bin/env bash
# validate_state.sh — Phase 5 guard
# Usage: ./scripts/validate_state.sh <DAY_NUMBER> [--audit]
#   <DAY_NUMBER>  Validate artifacts for a specific day
#   --audit       Scan ALL completed days in current_state.md and report gaps
# Exits 0 if all expected artifacts exist and current_state.md is consistent.
# Exits 1 with a descriptive error on any failure.

set -euo pipefail

MODE="${1:-}"
if [[ "$MODE" == "--audit" ]]; then
  STATE=".ai/current_state.md"
  ERRORS=()
  WARNINGS=()

  check_file() {
    local path="$1"; local label="$2"
    if [[ ! -f "$path" ]]; then ERRORS+=("MISSING $label: $path"); fi
  }

  echo "━━━ validate_state.sh --audit ━━━"
  # Extract all day numbers from Completed Days table (| NN | or | 0NN |)
  DAYS=$(grep -oE '^\| [0-9]{2} ' "$STATE" | tr -d '| ' | sort -u)
  if [[ -z "$DAYS" ]]; then
    echo "⚠️  No completed days found in $STATE"
    exit 0
  fi
  for DAY in $DAYS; do
    DAY_PAD=$(printf '%02d' "$DAY")
    echo ""
    echo "  Day ${DAY_PAD}:"
    SPEC="docs/architecture/day_${DAY_PAD}_spec.md"
    REPORT="docs/architecture/day_${DAY_PAD}_review_report.md"
    RUNBOOK="ops/runbooks/day_${DAY_PAD}_runbook.md"
    MISSING=""
    [[ -f "$SPEC" ]]    || { MISSING+=" spec"; }
    [[ -f "$REPORT" ]]  || { MISSING+=" review_report"; }
    [[ -f "$RUNBOOK" ]] || { MISSING+=" runbook"; }
    if [[ -n "$MISSING" ]]; then
      echo "    ⚠️  Missing:${MISSING}"
      WARNINGS+=("Day ${DAY_PAD}: missing${MISSING}")
    else
      echo "    ✅ All artifacts present"
    fi
  done
  echo ""
  if [[ ${#WARNINGS[@]} -eq 0 ]]; then
    echo "✅ Audit complete — no documentation gaps found."
    exit 0
  else
    echo "⚠️  ${#WARNINGS[@]} day(s) with documentation gaps (non-blocking):"
    for w in "${WARNINGS[@]}"; do echo "   • $w"; done
    echo ""
    echo "Reconcile gaps by creating missing artifacts on a fix branch."
    exit 0  # Audit mode: gaps are warnings, not failures
  fi
fi

DAY="${1:?Usage: validate_state.sh <DAY_NUMBER> [--audit]}"
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
check_grep "$STATE" "Incomplete Tasks"          "## Incomplete Tasks section"
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
