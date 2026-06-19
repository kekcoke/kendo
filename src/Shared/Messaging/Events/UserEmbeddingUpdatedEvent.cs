namespace Kendo.Shared.Messaging.Events;

/// <summary>
/// Published by UserService after a successful reindex (W5) or after a new user's
/// profile is embedded for the first time (W3). Consumed by the Worker
/// (UserEmbeddingUpdatedHandler — see day_19_spec.md), which emits a notification
/// when the embedding is for a new user and is otherwise a no-op audit log.
/// </summary>
public sealed record UserEmbeddingUpdatedEvent : KendoMessage
{
    /// <summary>The user whose embedding was updated.</summary>
    public Guid UserId { get; init; }

    /// <summary>Embedding model name.</summary>
    public string EmbeddingModel { get; init; } = "";

    /// <summary>Embedding dimensions.</summary>
    public int EmbeddingDimensions { get; init; }

    /// <summary>
    /// Trigger source: "first_embed" | "reindex" | "model_swap".
    /// </summary>
    public string Trigger { get; init; } = "";

    /// <summary>When the embedding was computed.</summary>
    public DateTimeOffset EmbeddedAt { get; init; }
}
