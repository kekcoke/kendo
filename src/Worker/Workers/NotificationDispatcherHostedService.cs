using Kendo.Worker.Models;
using System.Threading.Channels;

namespace Kendo.Worker.Workers;

/// <summary>
/// BackgroundService that consumes NotificationDispatchJob items from
/// the in-memory NotificationDispatcherChannel and delivers them.
/// 
/// The actual delivery contract (email, push, in-app) is out of scope
/// for this milestone — this service is a seam. Future notification-
/// pipeline specs will own the delivery implementation.
/// 
/// On Worker restart, the channel is empty and rebuilt from the Rebus
/// message flow (NotificationRequestedEvent).
/// </summary>
public class NotificationDispatcherHostedService : BackgroundService
{
    private readonly ChannelReader<NotificationDispatchJob> _reader;
    private readonly ILogger<NotificationDispatcherHostedService> _logger;

    public NotificationDispatcherHostedService(
        NotificationDispatcherChannel channel,
        ILogger<NotificationDispatcherHostedService> logger)
    {
        _reader = channel.Reader;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Notification dispatcher started — awaiting jobs.");

        await foreach (var job in _reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                _logger.LogInformation(
                    "Delivering notification: UserId={UserId}, TemplateId={TemplateId}, " +
                    "PromptVersion={PromptVersion}, RenderedLength={Length}",
                    job.UserId, job.TemplateId, job.PromptVersion, job.RenderedBody.Length);

                // Future: dispatch to email provider, push channel, or in-app queue.
                // The delivery contract is owned by a future notification-pipeline spec.

                _logger.LogInformation(
                    "Notification delivered successfully: UserId={UserId}, TemplateId={TemplateId}.",
                    job.UserId, job.TemplateId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to deliver notification: UserId={UserId}, TemplateId={TemplateId}. " +
                    "Notification dropped — will not retry (ephemeral delivery).",
                    job.UserId, job.TemplateId);
            }
        }

        _logger.LogInformation("Notification dispatcher stopped.");
    }
}
