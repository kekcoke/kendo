namespace Kendo.Shared.Messaging.Events;

/// <summary>
/// Published by the Gateway after W1 (Event Ingestion RAG) returns structured JSON
/// and the Gateway has validated the JSON shape. Consumed by the Worker
/// (EventIngestedHandler — see day_19_spec.md), which writes the row to UserService
/// and emits the follow-on EventValidatedEvent for W2.
///
/// Idempotency: EventId is the idempotency key. The handler upserts the Event row
/// by EventId and skips the embedding write if event_embeddings already has a row.
/// Re-delivery is safe.
/// </summary>
public sealed record EventIngestedEvent : KendoMessage
{
    /// <summary>Client-generated, idempotency-friendly event identifier.</summary>
    public Guid EventId { get; init; }

    /// <summary>Owner of the event.</summary>
    public Guid CreatedByUserId { get; init; }

    /// <summary>Ties to the originating request span.</summary>
    public Guid CorrelationId { get; init; }

    /// <summary>Raw free-form text from the user.</summary>
    public string SourceText { get; init; } = "";

    /// <summary>W1's structured Event JSON (validated).</summary>
    public string StructuredJson { get; init; } = "";

    /// <summary>Embedding model name, e.g. "BGE-large-en-v1.5" or "text-embedding-3-small".</summary>
    public string EmbeddingModel { get; init; } = "";

    /// <summary>Embedding dimensions: 1024 (local) or 1536 (cloud).</summary>
    public int EmbeddingDimensions { get; init; }

    /// <summary>pgvector-ready embedding array.</summary>
    public List<float> Embedding { get; init; } = new();

    /// <summary>When the ingestion occurred.</summary>
    public DateTimeOffset IngestedAt { get; init; }
}
