namespace Kendo.Shared.Http;

/// <summary>
/// A single SSE event from the FastAPI W7 notification summarization stream.
/// Parsed from event:/data: lines in the SSE stream.
/// </summary>
public class NotificationSummarizationChunk
{
    /// <summary>Event type: "chunk", "done", or "error".</summary>
    public string Event { get; set; } = "";

    /// <summary>Partial notification text (for "chunk" events).</summary>
    public string? Text { get; set; }

    /// <summary>Token count for this chunk (for "chunk" events).</summary>
    public int? TokenCount { get; set; }

    /// <summary>Full notification body (for "done" events).</summary>
    public string? NotificationBody { get; set; }

    /// <summary>Prompt version used to generate (for "done" events).</summary>
    public string? PromptVersion { get; set; }

    /// <summary>Total tokens in generated notification (for "done" events).</summary>
    public int? TotalTokens { get; set; }

    /// <summary>Trace ID for correlation (for "done" events).</summary>
    public string? TraceId { get; set; }

    /// <summary>RFC 7807 title (for "error" events).</summary>
    public string? Title { get; set; }

    /// <summary>RFC 7807 status code (for "error" events).</summary>
    public int? Status { get; set; }

    /// <summary>RFC 7807 detail message (for "error" events).</summary>
    public string? Detail { get; set; }
}
