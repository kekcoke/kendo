using Kendo.Shared.Messaging;
using Kendo.UserService.Data;
using Microsoft.EntityFrameworkCore;
using Rebus.Bus;

namespace Kendo.UserService.Services;

/// <summary>
/// Background service that polls the transactional outbox table for pending messages,
/// publishes them via Rebus, and marks them as processed.
/// 
/// Configuration (via appsettings.json or env vars):
/// - Relays__Outbox__PollingIntervalSeconds: how often to poll (default: 5)
/// - Relays__Outbox__BatchSize: max messages per batch (default: 20)
/// - Relays__Outbox__MaxRetries: max delivery attempts (default: 5)
/// </summary>
public class OutboxRelayService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxRelayService> _logger;
    private readonly int _pollingIntervalSeconds;
    private readonly int _batchSize;
    private readonly int _maxRetries;

    public OutboxRelayService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<OutboxRelayService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        _pollingIntervalSeconds = configuration.GetValue<int>("Relays__Outbox__PollingIntervalSeconds");
        if (_pollingIntervalSeconds <= 0) _pollingIntervalSeconds = 5;

        _batchSize = configuration.GetValue<int>("Relays__Outbox__BatchSize");
        if (_batchSize <= 0) _batchSize = 20;

        _maxRetries = configuration.GetValue<int>("Relays__Outbox__MaxRetries");
        if (_maxRetries <= 0) _maxRetries = 5;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "OutboxRelayService: Starting with pollingInterval={Interval}s, batchSize={BatchSize}, maxRetries={MaxRetries}",
            _pollingIntervalSeconds, _batchSize, _maxRetries);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OutboxRelayService: Unhandled error during batch processing. Will retry next cycle.");
            }

            await Task.Delay(TimeSpan.FromSeconds(_pollingIntervalSeconds), stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var resilientDb = scope.ServiceProvider.GetRequiredService<ResilientAppDbContext>();
        var bus = scope.ServiceProvider.GetService<IBus>();

        if (bus is null)
        {
            _logger.LogDebug("OutboxRelayService: IBus is null (ASB not configured). Skipping outbox processing.");
            return;
        }

        // Fetch pending messages ordered by creation time (FIFO)
        var pendingMessages = await resilientDb.ExecuteAsync(async ct =>
            await db.OutboxMessages
                .Where(m => m.ProcessedAt == null && m.RetryCount < _maxRetries)
                .OrderBy(m => m.CreatedAt)
                .Take(_batchSize)
                .ToListAsync(ct), stoppingToken);

        if (pendingMessages.Count == 0)
            return;

        _logger.LogDebug("OutboxRelayService: Processing {Count} pending outbox messages.", pendingMessages.Count);

        foreach (var outboxMessage in pendingMessages)
        {
            if (stoppingToken.IsCancellationRequested)
                break;

            try
            {
                var domainMessage = KendoMessageSerializer.Deserialize(outboxMessage.MessageType, outboxMessage.Payload);
                if (domainMessage is null)
                {
                    _logger.LogWarning(
                        "OutboxRelayService: Failed to deserialize message Id={MessageId}, Type={MessageType}. Skipping.",
                        outboxMessage.MessageId, outboxMessage.MessageType);

                    outboxMessage.RetryCount++;
                    outboxMessage.LastError = $"Failed to deserialize type '{outboxMessage.MessageType}'";
                    outboxMessage.ProcessedAt = DateTimeOffset.UtcNow; // Mark as processed to avoid retry loops
                    await resilientDb.ExecuteAsync(ct => db.SaveChangesAsync(ct), stoppingToken);
                    continue;
                }

                // Publish via Rebus
                await bus.Send(domainMessage);

                // Mark as processed
                outboxMessage.ProcessedAt = DateTimeOffset.UtcNow;
                await resilientDb.ExecuteAsync(ct => db.SaveChangesAsync(ct), stoppingToken);

                _logger.LogDebug(
                    "OutboxRelayService: Published message MessageId={MessageId}, Type={MessageType}.",
                    outboxMessage.MessageId, outboxMessage.MessageType);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "OutboxRelayService: Failed to publish message MessageId={MessageId}. Incrementing RetryCount.",
                    outboxMessage.MessageId);

                outboxMessage.RetryCount++;
                outboxMessage.LastError = ex.Message;

                if (outboxMessage.RetryCount >= _maxRetries)
                {
                    _logger.LogError(
                        "OutboxRelayService: Message MessageId={MessageId} exceeded max retries ({MaxRetries}). Skipping permanently.",
                        outboxMessage.MessageId, _maxRetries);
                    outboxMessage.ProcessedAt = DateTimeOffset.UtcNow;
                }

                await resilientDb.ExecuteAsync(ct => db.SaveChangesAsync(ct), stoppingToken);
            }
        }
    }
}
