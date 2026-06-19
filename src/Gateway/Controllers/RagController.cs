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
}
