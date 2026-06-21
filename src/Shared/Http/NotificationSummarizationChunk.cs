namespace Kendo.Shared.Http;

/// <summary>
/// A single SSE event from the FastAPI W7 notification summarization stream.
/// Parsed from event:/data: lines in the SSE stream.
/// </summary>
public class NotificationSummarizationChunk
{
    /// <summary>Event type: "chunk", "done", or "error".</summary>
    public string Event { get; init; } = "";

    /// <summary>Partial notification text (for "chunk" events).</summary>
    public string? Text { get; init; }

    /// <summary>Token count for this chunk (for "chunk" events).</summary>
    public int? TokenCount { get; init; }

    /// <summary>Full notification body (for "done" events).</summary>
    public string? NotificationBody { get; init; }

    /// <summary>Prompt version used to generate (for "done" events).</summary>
    public string? PromptVersion { get; init; }

    /// <summary>Total tokens in generated notification (for "done" events).</summary>
    public int? TotalTokens { get; init; }

    /// <summary>Trace ID for correlation (for "done" events).</summary>
    public string? TraceId { get; init; }

    /// <summary>RFC 7807 title (for "error" events).</summary>
    public string? Title { get; init; }

    /// <summary>RFC 7807 status code (for "error" events).</summary>
    public int? Status { get; init; }

    /// <summary>RFC 7807 detail message (for "error" events).</summary>
    public string? Detail { get; init; }
}
</RAW_375>
    }
  },
  {
    "name": "create_file",
    "kwargs": {
      "file_path": "src/Shared/Http/NotificationSummarizationOptions.cs",
      "new_string": <RAW_3795>namespace Kendo.Shared.Http;

/// <summary>
/// Configuration options for the W7 NotificationSummarizationClient.
/// Bound from Kendo:Notifications configuration section.
/// Separate from FastApiOptions and FastApiSummarizationOptions to allow
/// independent timeout/breaker tuning for the W7 summarization client.
/// </summary>
public class NotificationSummarizationOptions
{
    /// <summary>FastAPI base URL (Worker calls directly, not through Gateway).</summary>
    public string BaseUrl { get; set; } = "http://fastapi:8000";

    /// <summary>HTTP timeout in seconds for summarization requests.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Number of consecutive failures before circuit breaker opens.</summary>
    public int CircuitBreakerFailures { get; set; } = 3;

    /// <summary>Seconds circuit breaker stays open before half-open probe.</summary>
    public int CircuitBreakerBreakSeconds { get; set; } = 30;
}
</RAW_3795>
    }
  }
]