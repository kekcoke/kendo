using Npgsql;

namespace Kendo.UserService.Data;

/// <summary>
/// Factory for creating scoped Npgsql connections as the userservice_writer role.
/// This provides defense-in-depth behind the JWT admin:writes scope check.
/// The connection is scoped to a single request and disposed immediately after.
/// </summary>
public class AdminWriterConnectionFactory
{
    private readonly string _connectionString;

    public AdminWriterConnectionFactory(IConfiguration configuration)
    {
        // Build base connection string then override with userservice_writer credentials
        var baseConnectionString = configuration.GetConnectionString("DefaultConnection");
        var builder = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Username = "userservice_writer",
            Password = configuration["Kendo__AdminWriter__Password"] ?? "changeme_userservice_writer",
            Database = "kendo_users"
        };
        _connectionString = builder.ConnectionString;
    }

    /// <summary>
    /// Opens a new Npgsql connection authenticated as userservice_writer.
    /// Caller must dispose the connection.
    /// </summary>
    public async Task<NpgsqlConnection> OpenAsync(CancellationToken ct = default)
    {
        var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }
}
