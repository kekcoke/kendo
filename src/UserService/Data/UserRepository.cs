using Kendo.UserService.Models;
using Microsoft.EntityFrameworkCore;

namespace Kendo.UserService.Data;

public class UserRepository
{
    private readonly AppDbContext _db;
    private readonly ResilientAppDbContext _resilientDb;

    public UserRepository(AppDbContext db, ResilientAppDbContext resilientDb)
    {
        _db = db;
        _resilientDb = resilientDb;
    }

    public async Task<User> CreateAsync(string email, string displayName, CancellationToken ct = default)
    {
        var user = new User
        {
            Email = email,
            DisplayName = displayName,
            Status = UserStatus.Pending
        };

        _db.Users.Add(user);

        await _resilientDb.ExecuteAsync(async token =>
        {
            await _db.SaveChangesAsync(token);
        }, ct);

        return user;
    }

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _resilientDb.ExecuteAsync(async token =>
            await _db.Users.FirstOrDefaultAsync(u => u.Id == id, token), ct);
    }
}
