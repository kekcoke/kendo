namespace Kendo.Shared.Http;

/// <summary>
/// Typed client for the FastAPI service. Every method maps to a specific
/// FastAPI workload (W1–W7). Implemented in FastAPIClient with Polly
/// timeout + retry + circuit breaker.
/// </summary>
public interface IFastAPIClient
{
    // W1 — Event Ingestion RAG
    Task<EventIngestionResult> IngestEventAsync(EventIngestionRequest request, CancellationToken ct);
    IAsyncEnumerable<EventIngestionStreamChunk> IngestEventStreamAsync(EventIngestionRequest request, CancellationToken ct);

    // W2 — Event Conflict & Schedule Reasoning
    Task<EventValidationResult> ValidateEventAsync(Guid eventId, EventValidationRequest request, CancellationToken ct);

    // W3 — User Profile Semantic Search
    Task<UserSearchResult> SearchUsersAsync(UserSearchRequest request, CancellationToken ct);

    // W4 — User Intent Classification (advisory)
    Task<IntentClassificationResult> ClassifyIntentAsync(IntentClassificationRequest request, CancellationToken ct);

    // W6 — Document Q&A / Onboarding Assistant
    Task<AssistantAnswerResult> AskAssistantAsync(AssistantQuestionRequest request, CancellationToken ct);
}

// --- DTOs ---

public record EventIngestionRequest(string Text);
public record EventIngestionResult(string EventId, string StructuredJson);
public record EventIngestionStreamChunk(string Token, bool IsFinal);

public record EventValidationRequest(string EventPayload);
public record EventValidationResult(bool IsValid, string[] Conflicts, string Suggestions);

public record UserSearchRequest(string Query, int Top = 10);
public record UserSearchResult(string[] UserIds, double[] Relevance);

public record IntentClassificationRequest(string Body, string? SubjectId);
public record IntentClassificationResult(string Route, double Confidence);

public record AssistantQuestionRequest(string Question, string? ContextFile);
public record AssistantAnswerResult(string Answer, Citation[] Citations);

public record Citation(string File, int LineStart, int LineEnd, string Excerpt);
