namespace Kendo.Shared.Authentication;

/// <summary>
/// Configuration options for JWT authentication.
/// Bound from Kendo:Jwt configuration section.
/// </summary>
public class JwtOptions
{
    public string PrivateKeyPath { get; set; } = "secrets/jwt-private.pem";
    public string Issuer { get; set; } = "https://gateway.local/.well-known/jwks.json";
    public string Audience { get; set; } = "kendo.api";
    public int RotationDays { get; set; } = 90;
    public int TokenLifetimeMinutes { get; set; } = 60;
    public int ServiceTokenLifetimeSeconds { get; set; } = 60;
}
