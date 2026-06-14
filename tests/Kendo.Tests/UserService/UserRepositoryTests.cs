using Kendo.UserService.Data;
using Kendo.UserService.Models;
using Microsoft.EntityFrameworkCore;

namespace Kendo.Tests.UserService;

[Trait("Category", "Unit")]
public class UserRepositoryTests
{
    /// <summary>
    /// A pass-through resilience pipeline that executes the action directly.
    /// Used in tests to avoid mocking generic method signatures.
    /// </summary>
    private class PassThroughResiliencePipeline : Kendo.Shared.Resilience.IResiliencePipeline
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct = default)
            => action(ct);

        public Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken ct = default)
            => action(ct);
    }

    private static (AppDbContext db, ResilientAppDbContext resilient) CreateInMemoryDb(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        var db = new AppDbContext(options);
        var resilient = new ResilientAppDbContext(db, new PassThroughResiliencePipeline());
        return (db, resilient);
    }

    [Fact]
    public async Task CreateAsync_PersistsUserWithPendingStatus()
    {
        var (db, resilient) = CreateInMemoryDb(nameof(CreateAsync_PersistsUserWithPendingStatus));
        var repo = new UserRepository(db, resilient);

        var user = await repo.CreateAsync("test@example.com", "Test User");

        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Equal("test@example.com", user.Email);
        Assert.Equal("Test User", user.DisplayName);
        Assert.Equal(UserStatus.Pending, user.Status);
        Assert.True(user.CreatedAt != default);
        Assert.Null(user.ProcessedAt);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsUser_WhenExists()
    {
        var (db, resilient) = CreateInMemoryDb(nameof(GetByIdAsync_ReturnsUser_WhenExists));
        var repo = new UserRepository(db, resilient);

        var created = await repo.CreateAsync("found@example.com", "Found User");
        var found = await repo.GetByIdAsync(created.Id);

        Assert.NotNull(found);
        Assert.Equal(created.Id, found.Id);
        Assert.Equal("found@example.com", found.Email);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotExists()
    {
        var (db, resilient) = CreateInMemoryDb(nameof(GetByIdAsync_ReturnsNull_WhenNotExists));
        var repo = new UserRepository(db, resilient);

        var result = await repo.GetByIdAsync(Guid.NewGuid());

        Assert.Null(result);
    }
}
