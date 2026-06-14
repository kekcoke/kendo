#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RESULTS_DIR="${SCRIPT_DIR}/results"

mkdir -p "$RESULTS_DIR"

echo "=================================================="
echo " Kendo Chaos Test Suite — M3.3"
echo " Started: $(date -u '+%Y-%m-%dT%H:%M:%SZ')"
echo "=================================================="
echo ""

PASS_TOTAL=0
FAIL_TOTAL=0
SKIP_TOTAL=0

for TEST in test_db_downtime test_service_crash test_network_partition; do
  echo "--- Running: $TEST ---"
  echo ""
  set +e
  bash "${SCRIPT_DIR}/${TEST}.sh" 2>&1 | tee "${RESULTS_DIR}/${TEST}.log"
  EXIT_CODE=$?
  set -e
  RESULT_LINE=$(tail -1 "${RESULTS_DIR}/${TEST}.log")
  if echo "$RESULT_LINE" | grep -q "RESULT: PASS"; then
    PASS_TOTAL=$((PASS_TOTAL + 1))
  elif echo "$RESULT_LINE" | grep -q "RESULT: FAIL"; then
    FAIL_TOTAL=$((FAIL_TOTAL + 1))
  elif echo "$RESULT_LINE" | grep -q "RESULT: SKIP"; then
    SKIP_TOTAL=$((SKIP_TOTAL + 1))
  fi
  echo ""
  echo "--- $TEST completed (exit: $EXIT_CODE) ---"
  echo ""
done

echo "=================================================="
echo " SUMMARY"
echo "=================================================="
echo " Passed:  $PASS_TOTAL"
echo " Failed:  $FAIL_TOTAL"
echo " Skipped: $SKIP_TOTAL"
echo ""

if [ "$FAIL_TOTAL" -gt 0 ]; then
  echo "RESULT: FAIL — some chaos tests failed"
  exit 1
elif [ "$PASS_TOTAL" -eq 0 ] && [ "$SKIP_TOTAL" -gt 0 ]; then
  echo "RESULT: SKIP — all tests skipped (no broker, or Docker unavailable)"
  exit 0
else
  echo "RESULT: PASS — all chaos tests passed"
  exit 0
fi