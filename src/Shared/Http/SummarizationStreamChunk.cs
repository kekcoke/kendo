namespace Kendo.Shared.Http;

/// <summary>
/// A single SSE chunk from the FastAPI W7 notification summarization stream.
/// </summary>
public class SummarizationStreamChunk
{
    public string Text { get; init; } = "";
    public string PromptVersion { get; init; } = "v1";
}
