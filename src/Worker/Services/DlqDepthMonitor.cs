using Azure.Messaging.ServiceBus.Administration;
using Kendo.Shared.Messaging.Topology;

namespace Kendo.Worker.Services;

/// <summary>
/// Periodic background service that monitors Azure Service Bus Dead Letter Queue depths
/// for both the user-lifecycle queue (<c>kendo-events</c>) and the AI flow queue
/// (<c>kendo-events-ai</c>). When the depth of any queue exceeds the configured threshold,
/// an alert-level log is emitted identifying which queue is affected.
/// 
/// Configuration (via appsettings.json or env vars):
/// - <c>Rebus__ConnectionString</c>: ASB connection string (same as main Rebus config)
/// - <c>Rebus__DlqThreshold</c>: threshold for alerting (default: 5)
/// - <c>Rebus__DlqPollingIntervalSeconds</c>: polling interval (default: 60)
/// 
/// The monitor gracefully skips initialization when the connection string is empty/missing.
/// Per-queue alert state prevents alert storms: each queue has independent rate-limited
/// dedup so a single queue spike does not suppress alerts on the other.
/// </summary>
public class DlqDepthMonitor : BackgroundService
{
    private readonly string? _connectionString;
    private readonly int _threshold;
    private readonly int _pollingIntervalSeconds;
    private readonly ILogger<DlqDepthMonitor> _logger;

    // Alert state per queue (independent dedup so one queue's spike doesn't suppress the other)
    private sealed class QueueAlertState
    {
        public int LastAlertedDepth { get; set; }
        public bool HasAlerted { get; set; }
    }

    private readonly Dictionary<string, QueueAlertState> _alertStates = new()
    {
        [KendoTopology.UserLifecycleQueue] = new QueueAlertState(),
        [KendoTopology.AiFlowQueue] = new QueueAlertState(),
    };

    public DlqDepthMonitor(
        IConfiguration configuration,
        ILogger<DlqDepthMonitor> logger)
    {
        _connectionString = configuration["Rebus__ConnectionString"];
        _threshold = configuration.GetValue<int>("Rebus__DlqThreshold");
        if (_threshold <= 0) _threshold = 5; // default

        _pollingIntervalSeconds = configuration.GetValue<int>("Rebus__DlqPollingIntervalSeconds");
        if (_pollingIntervalSeconds <= 0) _pollingIntervalSeconds = 60; // default

        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            _logger.LogInformation(
                "DlqDepthMonitor: Rebus__ConnectionString is not configured. " +
                "DLQ monitoring is disabled. Set Rebus__ConnectionString to enable.");
            return;
        }

        _logger.LogInformation(
            "DlqDepthMonitor: Starting with threshold={Threshold}, pollingInterval={Interval}s. " +
            "Monitoring queues: {Queues}",
            _threshold, _pollingIntervalSeconds, string.Join(", ", _alertStates.Keys));

        var adminClient = new ServiceBusAdministrationClient(_connectionString);

        while (!stoppingToken.IsCancellationRequested)
        {
            await PollQueueDlqAsync(adminClient, KendoTopology.UserLifecycleQueue, stoppingToken);
            await PollQueueDlqAsync(adminClient, KendoTopology.AiFlowQueue, stoppingToken);

            await Task.Delay(TimeSpan.FromSeconds(_pollingIntervalSeconds), stoppingToken);
        }
    }

    private async Task PollQueueDlqAsync(
        ServiceBusAdministrationClient adminClient,
        string queueName,
        CancellationToken stoppingToken)
    {
        try
        {
            var runtimeProperties = await adminClient.GetQueueRuntimePropertiesAsync(
                queueName, stoppingToken);

            var dlqDepth = runtimeProperties.Value.DeadLetterMessageCount;
            var state = _alertStates[queueName];

            _logger.LogDebug(
                "DlqDepthMonitor: {QueueName} DLQ depth = {DlqDepth}, threshold = {Threshold}",
                queueName, dlqDepth, _threshold);

            if (dlqDepth > _threshold)
            {
                if (!state.HasAlerted || dlqDepth <= state.LastAlertedDepth)
                {
                    // Fire alert: either first time above threshold, or depth increased
                    _logger.LogError(
                        "DLQ ALERT: {QueueName} Dead Letter Queue depth ({DlqDepth}) " +
                        "exceeds threshold ({Threshold}). " +
                        "Investigate and drain the DLQ as soon as possible.",
                        queueName, dlqDepth, _threshold);

                    state.LastAlertedDepth = (int)dlqDepth;
                    state.HasAlerted = true;
                }
                else
                {
                    // Already alerted at this or higher depth — rate-limited
                    _logger.LogWarning(
                        "DLQ depth still elevated: {QueueName} depth={DlqDepth} (threshold={Threshold}). " +
                        "Alert was already fired at depth {LastAlertedDepth}.",
                        queueName, dlqDepth, _threshold, state.LastAlertedDepth);
                }
            }
            else
            {
                if (state.HasAlerted)
                {
                    _logger.LogInformation(
                        "DLQ depth dropped below threshold: {QueueName} depth={DlqDepth} <= {Threshold}. " +
                        "Resetting alert state.",
                        queueName, dlqDepth, _threshold);
                }

                state.LastAlertedDepth = 0;
                state.HasAlerted = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "DlqDepthMonitor: Failed to query DLQ depth for {QueueName}. " +
                "Will retry on next polling interval.",
                queueName);
        }
    }
}
