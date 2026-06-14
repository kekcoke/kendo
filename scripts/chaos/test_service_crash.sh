#!/bin/bash
set -euo pipefail

SCENARIO="Service crash — NGINX re-routes"
PASS_COUNT=0
FAIL_COUNT=0

echo "[STEP] Waiting for initial health..."
for i in $(seq 1 10); do
  READY=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:5000/health/live 2>/dev/null || echo "000")
  if [ "$READY" = "200" ]; then break; fi
  sleep 2
done

# Kill one user-service replica
REPLICA=$(docker compose ps --format '{{.Name}}' | grep userservice | head -2 | tail -1)
echo "[STEP] Killing replica: $REPLICA"
docker stop "$REPLICA" > /dev/null

echo "[STEP] Waiting for NGINX health check TTL..."
sleep 8

# Verify Gateway through NGINX still returns 200
ALL_OK=true
for i in $(seq 1 5); do
  GW_RESPONSE=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:5000/health/live 2>/dev/null || echo "000")
  if [ "$GW_RESPONSE" != "200" ]; then ALL_OK=false; fi
done

if [ "$ALL_OK" = true ]; then
  echo "[PASS] $SCENARIO — All 5 requests through NGINX returned 200"
  PASS_COUNT=$((PASS_COUNT + 1))
else
  echo "[FAIL] $SCENARIO — Not all requests returned 200"
  FAIL_COUNT=$((FAIL_COUNT + 1))
fi

# Verify X-Kendo-Replica headers show distribution from remaining replicas
HEADERS=$(for i in $(seq 1 5); do curl -s -I http://localhost:5000/health/live 2>/dev/null | grep -i "X-Kendo-Replica" | tr -d '\r'; done)
echo "Replica headers: $HEADERS"
if echo "$HEADERS" | grep -q "X-Kendo-Replica"; then
  echo "[PASS] $SCENARIO — Replica identity headers present post-crash"
  PASS_COUNT=$((PASS_COUNT + 1))
else
  echo "[FAIL] $SCENARIO — No replica identity headers visible"
  FAIL_COUNT=$((FAIL_COUNT + 1))
fi

echo "[STEP] Restoring killed replica..."
docker compose up -d --no-recreate --scale userservice=3 --no-deps userservice
sleep 8

echo ""
echo "RESULT: $([ $FAIL_COUNT -eq 0 ] && echo 'PASS' || echo 'FAIL') — $PASS_COUNT passed, $FAIL_COUNT failed, 0 skipped"
exit $([ $FAIL_COUNT -eq 0 ] && echo 0 || echo 1)
