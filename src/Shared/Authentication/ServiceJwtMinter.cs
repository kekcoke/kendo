using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Kendo.Shared.Authentication;

/// <summary>
/// Mints short-lived service JWTs for service-to-service auth.
/// Tokens carry scope: "admin:writes" and token_use: "service".
/// Default lifetime: 60 seconds; cached in-memory for 30 seconds.
/// </summary>
public interface IServiceJwtMinter
{
    string MintAdminWritesToken();
}

public class ServiceJwtMinter : IServiceJwtMinter
{
    private readonly RsaKeyProvider _keyProvider;
    private readonly JwtOptions _options;
    private string? _cachedToken;
    private DateTime _cachedUntil;

    public ServiceJwtMinter(RsaKeyProvider keyProvider, IOptions<JwtOptions> options)
    {
        _keyProvider = keyProvider;
        _options = options.Value;
        _cachedUntil = DateTime.MinValue;
    }

    public string MintAdminWritesToken()
    {
        if (!string.IsNullOrEmpty(_cachedToken) && DateTime.UtcNow < _cachedUntil)
        {
            return _cachedToken;
        }

        var now = DateTime.UtcNow;
        var signingKey = _keyProvider.GetCurrentSigningKey();

        var claims = new[]
        {
            new Claim("scope", "admin:writes"),
            new Claim("token_use", "service"),
            new Claim(JwtRegisteredClaimNames.Sub, "kendo-gateway"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: now.AddSeconds(_options.ServiceTokenLifetimeSeconds),
            notBefore: now,
            signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256)
        );

        _cachedToken = new JwtSecurityTokenHandler().WriteToken(token);
        // Cache for half the lifetime to avoid edge-case expiry
        _cachedUntil = now.AddSeconds(_options.ServiceTokenLifetimeSeconds / 2);

        return _cachedToken;
    }
}
