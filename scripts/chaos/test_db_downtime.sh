#!/bin/bash
set -euo pipefail

SCENARIO="DB downtime — circuit breaker fallback"
PASS_COUNT=0
FAIL_COUNT=0

echo "[STEP] Pausing PostgreSQL..."
docker compose pause postgres
echo "[STEP] Waiting for circuit breaker to trip (3 failures × ~2s)..."
sleep 8

# Test UserService direct health endpoint
US_RESPONSE=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:5001/health/ready || echo "000")
if [ "$US_RESPONSE" = "503" ]; then
  echo "[PASS] $SCENARIO — UserService returns 503 (circuit breaker fallback)"
  PASS_COUNT=$((PASS_COUNT + 1))
else
  echo "[FAIL] $SCENARIO — expected 503, got $US_RESPONSE"
  FAIL_COUNT=$((FAIL_COUNT + 1))
fi

# Test Gateway still serves (no cascade)
GW_RESPONSE=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:5000/health/live || echo "000")
if [ "$GW_RESPONSE" = "200" ]; then
  echo "[PASS] $SCENARIO — Gateway still serves 200 (no cascading failure)"
  PASS_COUNT=$((PASS_COUNT + 1))
else
  echo "[FAIL] $SCENARIO — expected 200 on Gateway, got $GW_RESPONSE"
  FAIL_COUNT=$((FAIL_COUNT + 1))
fi

echo "[STEP] Unpausing PostgreSQL..."
docker compose unpause postgres

echo "[STEP] Waiting for recovery..."
for i in $(seq 1 15); do
  READY=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:5001/health/ready 2>/dev/null || echo "000")
  if [ "$READY" = "200" ]; then
    echo "[PASS] $SCENARIO — System recovered after unpause"
    PASS_COUNT=$((PASS_COUNT + 1))
    break
  fi
  sleep 2
done

echo ""
echo "RESULT: $([ $FAIL_COUNT -eq 0 ] && echo 'PASS' || echo 'FAIL') — $PASS_COUNT passed, $FAIL_COUNT failed, 0 skipped"
exit $([ $FAIL_COUNT -eq 0 ] && echo 0 || echo 1)
