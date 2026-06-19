using Kendo.Shared.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kendo.Gateway.Controllers;

/// <summary>
/// Controller for W3 — User Profile Semantic Search.
/// Hybrid pgvector + BM25 search via FastAPI.
/// </summary>
[ApiController]
[Route("api/users")]
[Authorize]
public class UserSearchController : ControllerBase
{
    private readonly IFastAPIClient _fastApi;
    private readonly ILogger<UserSearchController> _logger;

    public UserSearchController(IFastAPIClient fastApi, ILogger<UserSearchController> logger)
    {
        _fastApi = fastApi;
        _logger = logger;
    }

    /// <summary>
    /// W3 — Search users by query string. Returns top-N user IDs with relevance scores.
    /// </summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] string q,
        [FromQuery] int top = 10,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest(new { error = "Query parameter 'q' is required" });

        try
        {
            var result = await _fastApi.SearchUsersAsync(new UserSearchRequest(q, top), ct);
            _logger.LogInformation("User search returned {Count} results for '{Query}'",
                result.UserIds.Length, q);

            return Ok(new
            {
                query = q,
                results = result.UserIds.Select((id, i) => new
                {
                    userId = id,
                    relevance = result.Relevance[i]
                }),
                total = result.UserIds.Length
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
