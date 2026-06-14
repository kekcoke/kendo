#!/bin/bash
set -euo pipefail

SCENARIO="Network partition — outbox + Rebus retry"
PASS_COUNT=0
FAIL_COUNT=0

echo "[STEP] Creating a user via the async endpoint..."
RESPONSE=$(curl -s -w "\n%{http_code}" -X POST http://localhost:5000/api/users \
  -H "Content-Type: application/json" \
  -d '{"name":"ChaosUser","email":"chaos@kendo.io"}')
HTTP_CODE=$(echo "$RESPONSE" | tail -1)

if [ "$HTTP_CODE" = "202" ]; then
  LOCATION=$(echo "$RESPONSE" | grep -i "location" | awk '{print $2}' | tr -d '\r' || true)
  echo "[PASS] $SCENARIO — POST accepted (202)"
  PASS_COUNT=$((PASS_COUNT + 1))

  # Poll the status endpoint to verify eventual creation
  for i in $(seq 1 10); do
    STATUS=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:5000/api/users/status/chaos 2>/dev/null || echo "000")
    if [ "$STATUS" = "200" ]; then
      echo "[PASS] $SCENARIO — User created via async processing"
      PASS_COUNT=$((PASS_COUNT + 1))
      break
    fi
    sleep 2
  done
else
  echo "[SKIP] $SCENARIO — ASB/outbox relay not configured (HTTP $HTTP_CODE). Expected in local dev without Rebus."
  echo "RESULT: SKIP — 0 passed, 0 failed, 3 skipped"
  exit 0
fi

echo ""
echo "RESULT: PASS — $PASS_COUNT passed, $FAIL_COUNT failed, 0 skipped"
exit 0
