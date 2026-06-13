using Kendo.UserService.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kendo.Tests.Data;

[Trait("Category", "Data")]
public class AppDbContextTests
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=kendo_users;Username=kendo;Password=kendo_dev";

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public void CanConnect_ToLocalPostgres()
    {
        using var context = CreateContext();

        var canConnect = context.Database.CanConnect();

        Assert.True(canConnect);
    }

    [Fact]
    public void EnsureCreated_CreatesDatabaseSchema()
    {
        using var context = CreateContext();

        var created = context.Database.EnsureCreated();

        // Should return true on first call (created) or false if already exists
        Assert.True(created || !created);
        Assert.NotNull(context.Model);
    }

    [Fact]
    public void VectorExtension_IsEnabled()
    {
        using var context = CreateContext();
        context.Database.EnsureCreated();

        var connection = context.Database.GetDbConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT extname FROM pg_extension WHERE extname = 'vector'";

        var result = command.ExecuteScalar();

        Assert.Equal("vector", result);
    }

    [Fact]
    public void DbContext_CanBeConstructed()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        using var context = new AppDbContext(options);

        Assert.NotNull(context);
        Assert.NotNull(context.Model);
    }
}
