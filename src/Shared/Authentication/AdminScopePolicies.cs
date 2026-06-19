using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Kendo.Shared.Authentication;

/// <summary>
/// Authorization policies for the admin:writes scope.
/// Enforces that the token has scope: "admin:writes" AND token_use: "service".
/// User JWTs with matching scope are rejected — only service JWTs pass.
/// </summary>
public static class AdminScopePoliciesExtensions
{
    public const string AdminWritesPolicy = "admin:writes";

    public static IServiceCollection AddKendoAdminScopePolicies(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(AdminWritesPolicy, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireClaim("scope", "admin:writes");
                policy.RequireAssertion(context =>
                {
                    var tokenUse = context.User.FindFirst("token_use")?.Value;
                    return tokenUse == "service";
                });
            });
        });

        return services;
    }
}
