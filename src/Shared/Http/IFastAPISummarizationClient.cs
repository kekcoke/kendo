using System.Runtime.CompilerServices;

namespace Kendo.Shared.Http;

/// <summary>
/// Worker-only FastAPI client for W7 (Notification Summarization).
/// Separate from the Gateway's IFastAPIClient — Worker does not
/// call W1–W6, so it only carries W7 DTOs.
/// </summary>
public interface IFastAPISummarizationClient
{
    IAsyncEnumerable<SummarizationStreamChunk> SummarizeStreamAsync(
        SummarizationRequest request, CancellationToken ct);
}
