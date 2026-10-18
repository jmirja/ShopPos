using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ShopPos.Application.Auth;
using ShopPos.Domain.Entities;            // if you used the fallback scaffold, change to the namespace of your entity classes
using ShopPos.Infrastructure.Persistence;
using ShopPos.Infrastructure.Security;

namespace ShopPos.Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly IDbContextFactory<ShopPosDbContext> _factory;
    private readonly PasswordHasher _hasher;
    private readonly Guid _defaultBranchId;

    public AuthService(IDbContextFactory<ShopPosDbContext> factory, PasswordHasher hasher, IConfiguration configuration)
    {
        _factory = factory;
        _hasher = hasher;
        _defaultBranchId = Guid.Parse(configuration["Shop:BranchId"] ?? "00000000-0000-0000-0000-000000000001");
    }

    public async Task<bool> HasAnyUserAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Users.AnyAsync();
    }

    public async Task CreateOwnerAsync(string fullName, string username, string password)
    {
        await using var db = await _factory.CreateDbContextAsync();

        if (await db.Users.AnyAsync())
            throw new InvalidOperationException("An account already exists.");

        var ownerRole = await db.Roles.SingleAsync(r => r.Name == "Owner");

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            BranchId = _defaultBranchId,
            RoleId = ownerRole.Id,
            Username = username.Trim(),
            PasswordHash = _hasher.Hash(password),
            FullName = fullName.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);

        db.AuditLogs.Add(new AuditLog
        {
            UserId = user.Id,
            Action = "Create",
            EntityName = "User",
            EntityId = user.Id.ToString(),
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
    }

    public async Task<AuthResult> LoginAsync(string username, string password)
    {
        await using var db = await _factory.CreateDbContextAsync();

        var name = username.Trim();
        var user = await db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Username == name);

        // Same message for "no such user" and "wrong password" so attackers learn nothing.
        if (user is null || user.IsActive != true || !_hasher.Verify(password, user.PasswordHash))
            return AuthResult.Fail("Wrong username or password.");

        db.AuditLogs.Add(new AuditLog
        {
            UserId = user.Id,
            Action = "Login",
            EntityName = "User",
            EntityId = user.Id.ToString(),
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        return new AuthResult(true, null, user.Id, user.FullName, user.Role.Name, user.BranchId);
    }
}
