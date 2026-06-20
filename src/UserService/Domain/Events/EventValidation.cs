namespace Kendo.UserService.Domain.Events;

public class EventValidation
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public bool Ok { get; set; }
    public string ConflictsJson { get; set; } = "[]";
    public string SuggestionsJson { get; set; } = "[]";
    public string? ReasoningTrace { get; set; }
    public DateTimeOffset ValidatedAt { get; set; }

    public Event Event { get; set; } = null!;
}
