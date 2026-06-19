using Kendo.Shared.Http;

namespace Kendo.Gateway.Middleware;

/// <summary>
/// W4 — Intent Advisory Middleware.
/// Calls FastAPI's /v1/intent/classify for ambiguous routes (≤80ms p95).
/// ALWAYS fails open — controller uses hard-coded fallback when FastAPI is down.
/// Tags HttpContext.Items for downstream controllers to read.
/// </summary>
public class IntentAdvisoryMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<IntentAdvisoryMiddleware> _logger;

    // Routes that benefit from intent classification
    private static readonly HashSet<string> AmbiguousPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/messages",
        "/api/events",
        "/api/ingest"
    };

    public IntentAdvisoryMiddleware(RequestDelegate next, ILogger<IntentAdvisoryMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IFastAPIClient fastApi)
    {
        // Fast path — skip for non-ambiguous routes, health endpoints, or GET requests
        if (!IsAmbiguousRoute(context.Request.Path) ||
            context.Request.Method == HttpMethod.Get.Method ||
            !context.User.Identity?.IsAuthenticated == true)
        {
            await _next(context);
            return;
        }

        try
        {
            // Read the request body for intent classification
            context.Request.EnableBuffering();
            using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0; // Reset for downstream

            var subjectId = context.User.FindFirst("sub")?.Value;

            var decision = await fastApi.ClassifyIntentAsync(
                new IntentClassificationRequest(body, subjectId),
                context.RequestAborted);

            // Tag the context for downstream controllers
            context.Items["kendo.intent.route"] = decision.Route;
            context.Items["kendo.intent.confidence"] = decision.Confidence;

            _logger.LogDebug(
                "Intent classified: route={Route}, confidence={Confidence}",
                decision.Route, decision.Confidence);
        }
        catch (Exception ex) when (ex is FastApiClientException or HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(
                "Intent advisory unavailable — using fallback. Reason: {Message}",
                ex.Message);

            // Fail open — controller falls back to hard-coded default
            context.Items["kendo.intent.route"] = "fallback";
            context.Items["kendo.intent.confidence"] = 0.0;
        }

        await _next(context);
    }

    private static bool IsAmbiguousRoute(PathString path)
    {
        return AmbiguousPaths.Any(p =>
            path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));
    }
}
