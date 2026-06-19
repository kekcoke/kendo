using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Kendo.Shared.Authentication;

/// <summary>
/// Extension method for registering JWT Bearer authentication with
/// Gateway-served JWKS key resolution.
/// </summary>
public static class AddKendoJwtExtensions
{
    public static IServiceCollection AddKendoJwt(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<JwtOptions>(config.GetSection("Kendo:Jwt"));
        services.AddSingleton<RsaKeyProvider>();

        var jwt = config.GetSection("Kendo:Jwt").Get<JwtOptions>() ?? new JwtOptions();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.RequireHttpsMetadata = false; // dev-friendly; prod uses HTTPS via NGINX

                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(2),
                    IssuerSigningKeyResolver = (token, securityToken, kid, parameters) =>
                    {
                        var provider = services.BuildServiceProvider()
                            .GetRequiredService<RsaKeyProvider>();
                        return new[] { provider.GetPublicKey() };
                    }
                };
            });

        return services;
    }
}
