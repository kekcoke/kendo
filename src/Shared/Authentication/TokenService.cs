using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Kendo.Shared.Authentication;

/// <summary>
/// Mints user JWTs signed with the Gateway's RSA keypair.
/// Validates input bounds before issuing. Uses the same RsaKeyProvider
/// as the existing JWT validation pipeline.
/// </summary>
public class TokenService : ITokenService
{
    private readonly RsaKeyProvider _keyProvider;
    private readonly JwtOptions _options;

    private const int MaxTtlSeconds = 86400; // 24h cap
    private const int MinTtlSeconds = 60;    // 1m floor

    public TokenService(RsaKeyProvider keyProvider, IOptions<JwtOptions> options)
    {
        _keyProvider = keyProvider;
        _options = options.Value;
    }

    public string IssueUserToken(string userId, string[] roles, int ttlSeconds)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("userId is required", nameof(userId));

        var clampedTtl = Math.Clamp(ttlSeconds, MinTtlSeconds, MaxTtlSeconds);
        var now = DateTime.UtcNow;
        var signingKey = _keyProvider.GetCurrentSigningKey();

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("token_use", "user"),
        };

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: now.AddSeconds(clampedTtl),
            notBefore: now,
            signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256)
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
