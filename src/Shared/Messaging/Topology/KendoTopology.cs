namespace Kendo.Shared.Messaging.Topology;

/// <summary>
/// Centralized constants for all queue and Dead Letter Queue names used by the
/// Kendo platform. Every consumer, producer, and monitor should reference these
/// constants rather than hardcoding queue names.
///
/// Design notes:
/// - <c>kendo-events-fastapi</c> is reserved for a future W7 audit flow.
/// - CCD-4: AI-mediated flows use a dedicated <c>kendo-events-ai</c> queue so a
///   poison message on one queue cannot starve the other.
/// </summary>
public static class KendoTopology
{
    /// <summary>Queue for user-lifecycle events (UserCreatedEvent, etc.).</summary>
    public const string UserLifecycleQueue = "kendo-events";

    /// <summary>DLQ for the user-lifecycle queue (auto-created by ASB).</summary>
    public const string UserLifecycleDlq = "kendo-events/$DeadLetterQueue";

    /// <summary>Queue for AI-mediated event flows (CCD-4 isolation).</summary>
    public const string AiFlowQueue = "kendo-events-ai";

    /// <summary>DLQ for the AI flow queue.</summary>
    public const string AiFlowDlq = "kendo-events-ai/$DeadLetterQueue";

    /// <summary>Reserved for future W7 notification summarization audit flow.</summary>
    public const string FastApiQueue = "kendo-events-fastapi";

    /// <summary>DLQ for the reserved FastAPI queue.</summary>
    public const string FastApiDlq = "kendo-events-fastapi/$DeadLetterQueue";
}
