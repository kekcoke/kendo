namespace Kendo.Shared.Messaging;

/// <summary>
/// Published when a new user registration is submitted via POST /api/users.
/// Consumed by the Worker for async processing (M2.3).
/// </summary>
public sealed record UserCreatedEvent : KendoMessage
{
    public Guid UserId { get; init; }
    public string Email { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
}
