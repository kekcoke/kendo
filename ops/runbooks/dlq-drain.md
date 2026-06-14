# DLQ Drain & Network Partition Recovery Playbook
> **Scenario:** Dead-letter queue depth exceeds threshold, or network partition separates the Worker from the message broker / database.  
> **Owners:** Platform Engineering / SRE  
> **Severity:** Medium — messages accumulating but no immediate data loss

---

## Detection

### Automated Signals
1. **DLQ depth alert** — `DlqDepthMonitor` BackgroundService logs `Error` level message when depth exceeds configurable threshold (default: 5). Rate-limited to one alert per 60s.
2. **DlqRecords table growth** — `SELECT COUNT(*) FROM "DlqRecords"` shows sustained increase across monitoring intervals.
3. **Consumer log errors** — `Worker` logs show `Message handling failed` or `Rebus: moving message to DLQ` entries.
4. **Outbox relay backlog** — `OutboxRelayService` logs show pending messages accumulating past expected processing time (default: 5s poll interval).

### Manual Signals
- Users report events not being processed (e.g., user created but welcome email never sent).
- Grafana dashboard shows DLQ depth graph spiking.
- CI network partition chaos test (`test_network_partition.sh`) fails.

---

## Triage Steps

```bash
# 1. Check DLQ monitor status
docker compose logs --tail=20 worker | grep -i "DlqDepthMonitor\|DLQ\|dead.letter"

# 2. Count DLQ records in database
docker compose exec postgres psql -U kendo -d kendo_users -c "SELECT COUNT(*) FROM \"DlqRecords\""

# 3. Check Worker connectivity to broker (if ASB configured)
docker compose logs --tail=30 worker | grep -i "Rebus\|ServiceBus\|connection\|error"

# 4. Check outbox relay status (UserService)
docker compose logs --tail=20 userservice | grep -i "OutboxRelay\|pending\|published"

# 5. Verify network connectivity
docker compose exec worker curl -s -o /dev/null -w "DB: %{http_code}\n" http://postgres:5432 2>/dev/null || echo "DB connectivity: FAIL"
```

---

## DLQ Drain Procedure

### Prerequisites
- DLQ consumer must be enabled: `Rebus__DlqConsumerEnabled` set to `true`
- Worker Service must be running

### Step 1: Enable DLQ Consumer (if disabled)

```bash
# Check current state
docker compose exec worker env | grep Rebus__DlqConsumerEnabled

# Enable via environment variable override
docker compose up -d --no-deps -e Rebus__DlqConsumerEnabled=true worker
```

### Step 2: Monitor DLQ Drain Progress

```bash
# Watch DLQ depth decrease
for i in $(seq 1 30); do
  DLQ_COUNT=$(docker compose exec postgres psql -U kendo -d kendo_users -t -c "SELECT COUNT(*) FROM \"DlqRecords\"" 2>/dev/null | tr -d ' ')
  echo "t+${i}s — DlqRecords: $DLQ_COUNT"
  if [ "$DLQ_COUNT" -eq 0 ]; then
    echo "DLQ fully drained"
    break
  fi
  sleep 10
done
```

### Step 3: Review Drained Messages

```bash
# Check last 10 DLQ records
docker compose exec postgres psql -U kendo -d kendo_users -c "
  SELECT \"Id\", \"MessageId\", \"MessageType\", \"SourceQueue\", \"FailedAt\", \"Reason\"
  FROM \"DlqRecords\"
  ORDER BY \"FailedAt\" DESC
  LIMIT 10;"
```

### Step 4: Investigate Root Cause

For each DLQ record, identify whether the message:
- **Should be reprocessed** — fix the handler bug, then re-enqueue messages via Rebus admin API
- **Is intentionally unrecoverable** — delete the record after documenting the reason
- **Is a duplicate / poison message** — discard after confirming idempotency key was handled

### Step 5: Disable DLQ Consumer (optional)

```bash
# If no longer needed
docker compose up -d --no-deps -e Rebus__DlqConsumerEnabled=false worker
```

---

## Network Partition Recovery

### Scenario A: Worker Cannot Reach Message Broker

**Symptom:** Rebus consumer logs connection timeouts; `DlqDepthMonitor` cannot poll ASB management API.

**Recovery:**

```bash
# 1. Verify broker connectivity
docker compose exec worker ping -c 2 <broker-host> 2>/dev/null || echo "Host unreachable"

# 2. Check DNS resolution
docker compose exec worker getent hosts <broker-host> 2>/dev/null || echo "DNS resolution failed"

# 3. Check firewall / network policy
# (Platform-specific — e.g., Azure NSG, Docker network, k8s NetworkPolicy)

# 4. Restart Worker to force Rebus reconnection
docker compose restart worker

# 5. After reconnection, verify Rebus consumer re-registered
docker compose logs --tail=10 worker | grep "Rebus\|starting\|consumer"
```

**Rebus retry behavior under partition:**
- Rebus uses exponential backoff for broker reconnection (starting at ~1s, max ~30s)
- Messages remain on the queue — no message loss during partition
- Outbox relay continues accumulating pending messages in the `OutboxMessages` table
- After reconnection: normal processing resumes, pending outbox messages are relayed

### Scenario B: Worker Cannot Reach Database

**Symptom:** Handler operations fail, messages are moved to DLQ after retry exhaustion.

**Recovery:**

```bash
# 1. Check DB connectivity
docker compose exec worker curl -s -o /dev/null -w "DB: %{http_code}\n" http://postgres:5432 2>/dev/null || echo "DB unreachable"

# 2. Check circuit breaker on Worker's DB client
docker compose logs --tail=20 worker | grep -i "CircuitBreaker\|database\|Polly"

# 3. Restore DB connectivity (see db-failover.md)

# 4. Verify messages in DLQ were recorded correctly
docker compose exec postgres psql -U kendo -d kendo_users -c "
  SELECT COUNT(*) AS dlq_messages, \"Reason\"
  FROM \"DlqRecords\"
  WHERE \"FailedAt\" > NOW() - INTERVAL '10 minutes'
  GROUP BY \"Reason\";"
```

### Scenario C: UserService / Gateway Network Partition

**Symptom:** Outbox relay cannot publish via Rebus; 202 Accepted responses are returned but events never reach the broker.

**Recovery:**

```bash
# 1. Check outbox relay status
docker compose logs --tail=20 userservice | grep -i "OutboxRelay\|publish\|pending\|error"

# 2. Check pending outbox messages
docker compose exec postgres psql -U kendo -d kendo_users -c "
  SELECT COUNT(*) AS pending_outbox
  FROM \"OutboxMessages\"
  WHERE \"ProcessedAt\" IS NULL;"

# 3. Restore broker connectivity (DNS, network, credentials)

# 4. Outbox relay reconnects automatically on next poll cycle (5s)
# Verify relay resumes:
docker compose logs --tail=10 userservice | grep "OutboxRelay\|published\|processed"

# 5. Confirm all pending messages were processed
docker compose exec postgres psql -U kendo -d kendo_users -c "
  SELECT COUNT(*) AS pending_outbox
  FROM \"OutboxMessages\"
  WHERE \"ProcessedAt\" IS NULL;"
```

---

## Post-Recovery Verification

```bash
# 1. All services healthy
docker compose ps

# 2. DLQ depth 0 or stable
docker compose exec postgres psql -U kendo -d kendo_users -t -c "SELECT COUNT(*) FROM \"DlqRecords\"" | tr -d ' '

# 3. Outbox messages fully processed
docker compose exec postgres psql -U kendo -d kendo_users -t -c "SELECT COUNT(*) FROM \"OutboxMessages\" WHERE \"ProcessedAt\" IS NULL" | tr -d ' '

# 4. Business endpoint healthy
curl -s -o /dev/null -w "POST /api/users: %{http_code}\n" \
  -X POST http://localhost:5000/api/users \
  -H "Content-Type: application/json" \
  -d '{"username":"dlq-recovery-test","email":"dlq@test.com"}'

# 5. Message consumed (check Worker logs after 10s)
sleep 10
docker compose logs --tail=5 worker | grep "dlq-recovery-test\|UserCreated"
```

---

## Post-Mortem Checklist

- [ ] Root cause of DLQ accumulation identified (handler bug, DB outage, config error)
- [ ] DLQ fully drained via consumer
- [ ] Outbox relay processed all pending messages
- [ ] All DLQ records reviewed: actionable vs. discard
- [ ] Network partition duration determined and documented
- [ ] Any unrecoverable messages documented with reason
- [ ] Alert threshold adjusted if needed (default: 5)
- [ ] Idempotency verified: no duplicate side effects from reprocessed messages
- [ ] Chaos network partition test (`test_network_partition.sh`) passes in CI

---

## Related Runbooks
- [`db-failover.md`](./db-failover.md) — for DB-level outages causing DLQ accumulation
- [`service-crash-recovery.md`](./service-crash-recovery.md) — for Worker crash recovery
- [`day_10_runbook.md`](./day_10_runbook.md) — outbox relay setup
- [`day_09_runbook.md`](./day_09_runbook.md) — DLQ consumer configuration
- [`day_16_runbook.md`](./day_16_runbook.md) — central recovery index
