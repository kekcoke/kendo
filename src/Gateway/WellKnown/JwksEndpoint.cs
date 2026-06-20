using Kendo.Shared.Authentication;
using Microsoft.IdentityModel.Tokens;

namespace Kendo.Gateway.WellKnown;

/// <summary>
/// Minimal API endpoint that publishes the Gateway's valid public RSA keys
/// in JWK format at /.well-known/jwks.json (RFC 7517).
/// During rotation overlap, returns both current and previous keys.
/// </summary>
public static class JwksEndpoint
{
    public static WebApplication MapJwksEndpoint(this WebApplication app)
    {
        app.MapGet("/.well-known/jwks.json", (RsaKeyProvider keys) =>
        {
            var validKeys = keys.GetAllValidPublicKeys();

            var jwkArray = validKeys.Select(kv =>
            {
                var (keyId, key) = kv;
                var rsa = key.Rsa ?? throw new InvalidOperationException($"RSA key {keyId} not initialized");

                var parameters = rsa.ExportParameters(includePrivateParameters: false);

                var n = Base64UrlEncoder.Encode(parameters.Modulus!);
                var e = Base64UrlEncoder.Encode(parameters.Exponent!);

                return new
                {
                    kty = "RSA",
                    use = "sig",
                    alg = "RS256",
                    kid = keyId,
                    n,
                    e
                };
            }).ToArray();

            return Results.Json(new { keys = jwkArray });
        }).AllowAnonymous();

        return app;
    }
}
