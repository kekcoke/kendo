using System.Threading.Channels;
using Kendo.Worker.Models;

namespace Kendo.Worker.Workers;

/// <summary>
/// In-memory channel for NotificationDispatchJob items.
/// Singleton-registered — the same channel is shared between the
/// NotificationRequestedHandler (producer) and the
/// NotificationDispatcherHostedService (consumer).
/// 
/// Channel is unbounded to avoid blocking Rebus message handlers.
/// Notifications are ephemeral; a Worker restart rebuilds the channel
/// from the Rebus message flow.
/// </summary>
public class NotificationDispatcherChannel
{
    public Channel<NotificationDispatchJob> Channel { get; }

    public ChannelWriter<NotificationDispatchJob> Writer => Channel.Writer;
    public ChannelReader<NotificationDispatchJob> Reader => Channel.Reader;

    public NotificationDispatcherChannel()
    {
        Channel = System.Threading.Channels.Channel.CreateUnbounded<NotificationDispatchJob>(
            new UnboundedChannelOptions
            {
                SingleWriter = false,
                SingleReader = true
            });
    }
}
