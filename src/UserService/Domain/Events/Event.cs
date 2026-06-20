using Kendo.UserService.Models;

namespace Kendo.UserService.Domain.Events;

public enum EventIngestionStatus
{
    Pending,
    Validated,
    Rejected,
    Failed
}

public class Event
{
    public Guid Id { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public string? Location { get; set; }
    public int? Headcount { get; set; }
    public string? DietaryNotes { get; set; }
    public string SourceText { get; set; } = string.Empty;
    public EventIngestionStatus IngestionStatus { get; set; } = EventIngestionStatus.Pending;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public User CreatedBy { get; set; } = null!;
    public EventValidation? Validation { get; set; }
}
