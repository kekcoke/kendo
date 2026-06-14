using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Options;

namespace Kendo.Worker.Services;

/// <summary>
/// Periodic background service that monitors the Azure Service Bus Dead Letter Queue depth.
/// When the depth exceeds the configured threshold, an alert-level log is emitted.
/// 
/// Configuration (via appsettings.json or env vars):
/// - Rebus__ConnectionString: ASB connection string (same as main Rebus config)
/// - Rebus__DlqThreshold: threshold for alerting (default: 5)
/// - Rebus__DlqPollingIntervalSeconds: polling interval (default: 60)
/// 
/// The monitor gracefully skips initialization when the connection string is empty/missing.
/// </summary>
public class DlqDepthMonitor : BackgroundService
{
    private const string QueueName = "kendo-events";
    private readonly string? _connectionString;
    private readonly int _threshold;
    private readonly int _pollingIntervalSeconds;
    private readonly ILogger<DlqDepthMonitor> _logger;
    private int _lastAlertedDepth;
    private bool _hasAlerted;

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
        _lastAlertedDepth = 0;
        _hasAlerted = false;
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
            "DlqDepthMonitor: Starting with threshold={Threshold}, pollingInterval={Interval}s",
            _threshold, _pollingIntervalSeconds);

        var adminClient = new ServiceBusAdministrationClient(_connectionString);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var runtimeProperties = await adminClient.GetQueueRuntimePropertiesAsync(
                    QueueName, stoppingToken);

                var dlqDepth = runtimeProperties.Value.DeadLetterMessageCount;

                _logger.LogDebug(
                    "DlqDepthMonitor: kendo-events DLQ depth = {DlqDepth}, threshold = {Threshold}",
                    dlqDepth, _threshold);

                if (dlqDepth > _threshold)
                {
                    if (!_hasAlerted || dlqDepth <= _lastAlertedDepth)
                    {
                        // Fire alert: either first time above threshold, or depth increased
                        _logger.LogError(
                            "DLQ ALERT: kendo-events Dead Letter Queue depth ({DlqDepth}) " +
                            "exceeds threshold ({Threshold}). " +
                            "Investigate and drain the DLQ as soon as possible.",
                            dlqDepth, _threshold);

                        _lastAlertedDepth = (int)dlqDepth;
                        _hasAlerted = true;
                    }
                    else
                    {
                        // Already alerted at this or higher depth — rate-limited
                        _logger.LogWarning(
                            "DLQ depth still elevated: {DlqDepth} (threshold={Threshold}). " +
                            "Alert was already fired at depth {LastAlertedDepth}.",
                            dlqDepth, _threshold, _lastAlertedDepth);
                    }
                }
                else
                {
                    if (_hasAlerted)
                    {
                        _logger.LogInformation(
                            "DLQ depth dropped below threshold: {DlqDepth} <= {Threshold}. " +
                            "Resetting alert state.",
                            dlqDepth, _threshold);
                    }

                    _lastAlertedDepth = 0;
                    _hasAlerted = false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "DlqDepthMonitor: Failed to query DLQ depth for kendo-events. " +
                    "Will retry on next polling interval.");
            }

            await Task.Delay(TimeSpan.FromSeconds(_pollingIntervalSeconds), stoppingToken);
        }
    }
}
