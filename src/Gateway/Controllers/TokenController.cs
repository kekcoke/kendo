using Kendo.Shared.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kendo.Gateway.Controllers;

/// <summary>
/// Endpoint for minting user JWTs. Protected by admin:token scope —
/// only service JWTs (Worker, UserService) may call this endpoint.
/// </summary>
[ApiController]
[Route("api/auth")]
[Authorize(Policy = "AdminToken")]
public class TokenController : ControllerBase
{
    private readonly ITokenService _tokenService;

    public TokenController(ITokenService tokenService)
    {
        _tokenService = tokenService;
    }

    /// <summary>
    /// Issue a user JWT. Caller must present a Gateway service-JWT
    /// with admin:token scope.
    /// </summary>
    [HttpPost("token")]
    public IActionResult IssueToken([FromBody] TokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
            return Problem(
                title: "Bad Request",
                detail: "userId is required",
                statusCode: StatusCodes.Status400BadRequest);

        if (request.Roles is null || request.Roles.Length == 0)
            return Problem(
                title: "Bad Request",
                detail: "At least one role is required",
                statusCode: StatusCodes.Status400BadRequest);

        var clampedTtl = Math.Clamp(request.TtlSeconds, 60, 86400);
        var token = _tokenService.IssueUserToken(request.UserId, request.Roles, clampedTtl);

        return Ok(new TokenResponse
        {
            AccessToken = token,
            TokenType = "Bearer",
            ExpiresIn = clampedTtl
        });
    }
}

public class TokenRequest
{
    public string UserId { get; set; } = string.Empty;
    public string[] Roles { get; set; } = Array.Empty<string>();
    public int TtlSeconds { get; set; } = 3600;
}

public class TokenResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";
    public int ExpiresIn { get; set; }
}
