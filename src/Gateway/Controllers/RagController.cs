using Kendo.Shared.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kendo.Gateway.Controllers;

/// <summary>
/// Controller for W1 (Event Ingestion RAG) and W2 (Event Validation).
/// Returns 202 Accepted with Location header per async pattern (Day 07).
/// </summary>
[ApiController]
[Route("api")]
[Authorize]
public class RagController : ControllerBase
{
    private readonly IFastAPIClient _fastApi;
    private readonly ILogger<RagController> _logger;

    public RagController(IFastAPIClient fastApi, ILogger<RagController> logger)
    {
        _fastApi = fastApi;
        _logger = logger;
    }

    /// <summary>
    /// W1 — Event Ingestion RAG. POST free-form text → structured Event JSON.
    /// Returns 202 with Location header for status polling.
    /// </summary>
    [HttpPost("events/ingest")]
    public async Task<IActionResult> IngestEvent(
        [FromBody] EventIngestionRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(new { error = "Text is required" });

        try
        {
            var result = await _fastApi.IngestEventAsync(request, ct);
            _logger.LogInformation("Event ingested: {EventId}", result.EventId);

            // 202 Accepted with Location header
            Response.Headers["Location"] = $"/api/events/{result.EventId}";
            return Accepted(new
            {
                eventId = result.EventId,
                structuredJson = result.StructuredJson,
                status = "processing"
            });
        }
        catch (FastApiClientException ex)
        {
            return StatusCode(ex.Error.StatusCode, new
            {
                error = ex.Error.Title,
                detail = ex.Error.Detail
            });
        }
    }

    /// <summary>
    /// SSE stream of W1 inference (token-by-token).
    /// </summary>
    [HttpGet("events/ingest/{id:guid}/stream")]
    public async Task IngestEventStream(
        Guid id,
        CancellationToken ct)
    {
        Response.Headers["Content-Type"] = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["Connection"] = "keep-alive";

        try
        {
            await foreach (var chunk in _fastApi.IngestEventStreamAsync(
                new EventIngestionRequest(""), ct))
            {
                await Response.WriteAsync(
                    $"data: {System.Text.Json.JsonSerializer.Serialize(chunk)}\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }
        }
        catch (FastApiClientException ex)
        {
            await Response.WriteAsync(
                $"data: {{\"error\":\"{ex.Error.Title}\",\"detail\":\"{ex.Error.Detail}\"}}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
    }

    /// <summary>
    /// W2 — Event Conflict & Schedule Reasoning.
    /// Validates a candidate event for conflicts and suggestions.
    /// </summary>
    [HttpPost("events/{id:guid}/validate")]
    public async Task<IActionResult> ValidateEvent(
        Guid id,
        [FromBody] EventValidationRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await _fastApi.ValidateEventAsync(id, request, ct);
            _logger.LogInformation("Event {EventId} validated: {IsValid}", id, result.IsValid);

            return Ok(new
            {
                isValid = result.IsValid,
                conflicts = result.Conflicts,
                suggestions = result.Suggestions
            });
        }
        catch (FastApiClientException ex)
        {
            return StatusCode(ex.Error.StatusCode, new
            {
                error = ex.Error.Title,
                detail = ex.Error.Detail
            });
        }
    }

    // ============================================================
    // M5.4 — Generic RAG routes (proxied to FastAPI /v1/rag/*)
    // ============================================================

    /// <summary>
    /// M5.4 — Synchronous RAG query. GET with query params, proxies to FastAPI POST /v1/rag/query.
    /// </summary>
    [HttpGet("rag/query")]
    public async Task<IActionResult> RagQuery(
        [FromQuery] string q,
        [FromQuery] int top_k = 5,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest(new { error = "Query parameter 'q' is required" });

        try
        {
            var result = await _fastApi.RagQueryAsync(
                new RagQueryRequest(q, top_k), ct);

            _logger.LogInformation("RAG query: top_k={TopK}, trace={TraceId}", top_k, result.TraceId);

            return Ok(new
            {
                answer = result.Answer,
                contexts = result.Contexts,
                traceId = result.TraceId
            });
        }
        catch (FastApiClientException ex)
        {
            return StatusCode(ex.Error.StatusCode, new
            {
                error = ex.Error.Title,
                detail = ex.Error.Detail
            });
        }
    }

    /// <summary>
    /// M5.4 — Streaming RAG query (SSE). GET with query params, proxies to FastAPI POST /v1/rag/stream.
    /// </summary>
    [HttpGet("rag/stream")]
    public async Task RagQueryStream(
        [FromQuery] string q,
        [FromQuery] int top_k = 5,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            Response.StatusCode = 400;
            await Response.WriteAsync(
                "data: {\"type\":\"error\",\"text\":\"Query parameter 'q' is required\",\"isDone\":true}\n\n", ct);
            await Response.Body.FlushAsync(ct);
            return;
        }

        Response.Headers["Content-Type"] = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["Connection"] = "keep-alive";

        try
        {
            await foreach (var chunk in _fastApi.RagQueryStreamAsync(
                new RagQueryRequest(q, top_k), ct))
            {
                await Response.WriteAsync(
                    $"data: {System.Text.Json.JsonSerializer.Serialize(chunk)}\n\n", ct);
                await Response.Body.FlushAsync(ct);

                if (chunk.IsDone) break;
            }
        }
        catch (FastApiClientException ex)
        {
            await Response.WriteAsync(
                $"data: {{\"type\":\"error\",\"text\":\"{ex.Error.Title}: {ex.Error.Detail}\",\"isDone\":true}}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
    }
}
