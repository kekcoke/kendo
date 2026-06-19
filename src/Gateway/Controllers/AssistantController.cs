using Kendo.Shared.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kendo.Gateway.Controllers;

/// <summary>
/// Controller for W6 — Document Q&A / Onboarding Assistant.
/// Internal RAG over docs, runbooks, and skill templates.
/// </summary>
[ApiController]
[Route("api/assistant")]
[Authorize]
public class AssistantController : ControllerBase
{
    private readonly IFastAPIClient _fastApi;
    private readonly ILogger<AssistantController> _logger;

    public AssistantController(IFastAPIClient fastApi, ILogger<AssistantController> logger)
    {
        _fastApi = fastApi;
        _logger = logger;
    }

    /// <summary>
    /// W6 — Ask the onboarding/documentation assistant.
    /// Returns cited answers with file + line ranges.
    /// </summary>
    [HttpPost("ask")]
    public async Task<IActionResult> Ask(
        [FromBody] AssistantQuestionRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return BadRequest(new { error = "Question is required" });

        try
        {
            var result = await _fastApi.AskAssistantAsync(request, ct);
            _logger.LogInformation("Assistant answered question ({Citations} citations)",
                result.Citations.Length);

            return Ok(new
            {
                answer = result.Answer,
                citations = result.Citations.Select(c => new
                {
                    file = c.File,
                    lineStart = c.LineStart,
                    lineEnd = c.LineEnd,
                    excerpt = c.Excerpt
                })
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
