namespace Kendo.Worker.Models;

/// <summary>
/// A rendered notification ready for delivery.
/// Enqueued by NotificationRequestedHandler and consumed by
/// NotificationDispatcherHostedService.
/// </summary>
public class NotificationDispatchJob
{
    public Guid UserId { get; init; }
    public string TemplateId { get; init; } = "";
    public string RenderedBody { get; init; } = "";
    public string PromptVersion { get; init; } = "v1";
    public DateTimeOffset RequestedAt { get; init; }
}
