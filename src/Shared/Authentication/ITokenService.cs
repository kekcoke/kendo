namespace Kendo.Shared.Authentication;

/// <summary>
/// Service for minting user JWTs signed by the Gateway's RSA keypair.
/// Caller must present a service-JWT with admin:token scope.
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Issues a user JWT with the specified claims.
    /// </summary>
    /// <param name="userId">The user's unique identifier.</param>
    /// <param name="roles">Role claims to include (e.g. "user", "admin").</param>
    /// <param name="ttlSeconds">Token lifetime in seconds (capped by config max).</param>
    /// <returns>A signed RS256 JWT string.</returns>
    string IssueUserToken(string userId, string[] roles, int ttlSeconds);
}
