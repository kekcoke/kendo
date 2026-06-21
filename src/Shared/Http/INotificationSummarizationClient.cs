using System.Runtime.CompilerServices;

namespace Kendo.Shared.Http;

/// <summary>
/// Worker-only client for W7 notification summarization.
/// Calls FastAPI directly (not through Gateway) and parses SSE stream
/// with proper event:/data: line handling.
/// Separate from IFastAPISummarizationClient — uses correct SSE parsing.
/// </summary>
public interface INotificationSummarizationClient
{
    /// <summary>
    /// Calls FastAPI /v1/notifications/summarize and yields SSE chunks.
    /// Each chunk represents a partial notification text token.
    /// The final "done" event contains the full notification_body and metadata.
    /// On error, an "error" event is yielded with RFC 7807 Problem Details.
    /// </summary>
    IAsyncEnumerable<NotificationSummarizationChunk> SummarizeStreamAsync(
        NotificationSummarizationRequest request, CancellationToken ct);
}
