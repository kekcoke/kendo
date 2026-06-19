using Kendo.Shared.Authentication;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;

namespace Kendo.Gateway.WellKnown;

/// <summary>
/// Minimal API endpoint that publishes the Gateway's public RSA key
/// in JWK format at /.well-known/jwks.json (RFC 7517).
/// </summary>
public static class JwksEndpoint
{
    public static WebApplication MapJwksEndpoint(this WebApplication app)
    {
        app.MapGet("/.well-known/jwks.json", (RsaKeyProvider keys) =>
        {
            var publicKey = keys.GetPublicKey();
            var rsa = publicKey.Rsa ?? throw new InvalidOperationException("RSA key not initialized");

            var parameters = rsa.ExportParameters(includePrivateParameters: false);

            // Base64url encode the modulus and exponent per RFC 7518
            var n = Base64UrlEncoder.Encode(parameters.Modulus!);
            var e = Base64UrlEncoder.Encode(parameters.Exponent!);

            var jwk = new
            {
                keys = new[]
                {
                    new
                    {
                        kty = "RSA",
                        use = "sig",
                        alg = "RS256",
                        kid = keys.CurrentKeyId(),
                        n,
                        e
                    }
                }
            };

            return Results.Json(jwk);
        }).AllowAnonymous();

        return app;
    }
}
