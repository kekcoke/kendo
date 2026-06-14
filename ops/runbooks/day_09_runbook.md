# Day 09 Runbook — Dead Letter Queue (DLQ) Consumer & Alerting

**Milestone:** M2.4 — DLQ consumer implemented; alert fires when DLQ depth exceeds threshold

---

## Overview

This runbook covers the DLQ infrastructure added in Day 09:
- `DeadLetterHandler` — Rebus consumer that processes messages from the Azure Service Bus DLQ (`kendo-events/$DeadLetterQueue`)
- `DlqDepthMonitor` — background service that periodically queries DLQ depth and logs an alert when the configured threshold is exceeded

---

## DLQ Consumer

### How it works

When a message delivery fails repeatedly (ASB default: 10 retries), ASB automatically moves the message to the Dead Letter Queue at `kendo-events/$DeadLetterQueue`. The `DeadLetterHandler` (registered via `AddKendoRebusDlqConsumer()`) consumes messages from this queue.

For each DLQ message, the handler:
1. Logs at `LogLevel.Error` with full DLQ metadata (OriginalMessageId, OriginalMessageType, DeadLetterReason, DeadLetterErrorDescription, DeliveryCount, EnqueuedTime)
2. Persists a `DlqRecord` to the database for audit trail and reference
3. Acknowledges the message (fail-safe — prevents re-delivery loops)

### Configuration

```json
{
  "Rebus": {
    "DlqConsumerEnabled": false,   // Must be set to true to activate DLQ consumer
    "DlqConnectionString": ""       // Defaults to Rebus__ConnectionString if empty
  }
}
```

> **Important:** The DLQ consumer is **disabled by default**. Set `Rebus__DlqConsumerEnabled: true` in your environment or appsettings to enable it. This prevents accidental DLQ consumption in local development environments.

### Enabling the DLQ consumer

```bash
# Via environment variable (production)
export Rebus__DlqConsumerEnabled=true

# Via appsettings.Production.json
# "Rebus__DlqConsumerEnabled": true

# Via dotnet user-secrets (local test)
dotnet user-secrets set "Rebus__DlqConsumerEnabled" "true" --project src/Worker
```

---

## DLQ Depth Monitor

### How it works

The `DlqDepthMonitor` is a `BackgroundService` that polls the ASB management API at a configurable interval to check the DLQ depth of the `kendo-events` queue.

When depth exceeds the threshold:
1. A `LogLevel.Error` alert is emitted with the current depth and threshold
2. Subsequent polls above threshold suppress duplicate alerts (rate-limited) until the depth drops below threshold and rises again

When depth returns below threshold:
1. A `LogLevel.Information` message is logged indicating the alert state has reset
2. The alert cooldown is cleared, allowing a new alert on the next spike

### Configuration

```json
{
  "Rebus": {
    "ConnectionString": "Endpoint=sb://...",  // Required for DLQ monitoring
    "DlqThreshold": 5,                        // Alert when depth exceeds this (env: Rebus__DlqThreshold)
    "DlqPollingIntervalSeconds": 60           // How often to check (env: Rebus__DlqPollingIntervalSeconds)
  }
}
```

### Verification

```bash
# Check if monitor is running (look for startup log)
docker logs kendo-worker | grep "DlqDepthMonitor"
# Expected: "DlqDepthMonitor: Starting with threshold=5, pollingInterval=60s"

# Check for DLQ alert logs
docker logs kendo-worker | grep "DLQ ALERT"
```

---

## DLQ Recovery Procedure

### Step 1 — Identify the cause

Check the Worker logs for the `DeadLetterReason` and `DeadLetterErrorDescription`:

```bash
docker logs kendo-worker | grep "DLQ message received"
```

Common reasons:
- **MaxDeliveryCountExceeded** — Handler threw an exception repeatedly. Check `UserCreatedEventHandler` logs for the underlying error.
- **SessionHasLocked** — Session lock lost during processing. Usually transient — can be safely re-queued.

### Step 2 — Drain the DLQ

To re-process DLQ messages, either:

**Option A — Manual re-queue (recommended for single messages):**
Use the Azure Portal or Azure CLI to re-queue messages from the DLQ back to the main queue.

```bash
# Using az CLI (requires az servicebus topic subscription deadletter ...)
# Or use Azure Portal: Service Bus → kendo-events → Dead-letter queue → Re-queue
```

**Option B — Automated drain (for bulk recovery):**
Deploy a one-shot console app or script that reads from the DLQ and forwards to the main queue. The `DeadLetterHandler` already logs all DLQ metadata — use those logs as the source of truth for which messages need re-queuing.

### Step 3 — Monitor re-processing

After re-queuing, monitor the Worker logs to verify messages are processed successfully:

```bash
docker logs -f kendo-worker | grep -E "Processing|Successfully processed|DLQ ALERT"
```

### Step 4 — Verify DLQ depth cleared

```bash
docker logs kendo-worker | grep "DLQ depth dropped below threshold"
```

---

## Alerting Configuration

### Log-based alerts

The DLQ monitor emits structured logs at these levels:

| Log Level | Condition | Message |
|---|---|---|
| `Error` | DLQ depth exceeds threshold (first time or depth increased) | `DLQ ALERT: kendo-events Dead Letter Queue depth ({depth}) exceeds threshold ({threshold})...` |
| `Warning` | DLQ depth still elevated (alert already fired) | `DLQ depth still elevated: {depth} (threshold={threshold}). Alert was already fired at depth {lastAlertedDepth}.` |
| `Warning` | Failed to query DLQ depth | `DlqDepthMonitor: Failed to query DLQ depth for kendo-events. Will retry on next polling interval.` |
| `Information` | DLQ depth dropped below threshold | `DLQ depth dropped below threshold: {depth} <= {threshold}. Resetting alert state.` |

### Integration targets

- **CloudWatch / Azure Monitor:** Forward `LogLevel.Error` logs containing "DLQ ALERT" to your monitoring platform
- **PagerDuty / OpsGenie:** Create alert rules that trigger on "DLQ ALERT" log entries
- **Slack / Teams:** Subscribe to `LogLevel.Error` logs from the Worker service

---

## Configuration Reference

| Environment Variable | Default | Description |
|---|---|---|
| `Rebus__ConnectionString` | (required) | ASB connection string for main queue and DLQ |
| `Rebus__DlqConsumerEnabled` | `false` | Enable DLQ consumer (opt-in) |
| `Rebus__DlqThreshold` | `5` | DLQ depth alert threshold |
| `Rebus__DlqPollingIntervalSeconds` | `60` | Polling interval in seconds |
