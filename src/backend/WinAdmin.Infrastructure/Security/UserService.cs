using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Infrastructure.Security;

public sealed class UserService : IUserService
{
    private readonly WinAdminDbContext _db;
    private readonly PasswordHasher<UserEntity> _hasher = new();

    // Хеш для несуществующего логина: время ответа не выдаёт, есть ли такой пользователь.
    private static readonly Lazy<string> DummyHash = new(() =>
        new PasswordHasher<UserEntity>().HashPassword(new UserEntity(), Guid.NewGuid().ToString()));

    public UserService(WinAdminDbContext db) => _db = db;

    public async Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken ct = default)
    {
        var users = await _db.Users.AsNoTracking().OrderBy(u => u.Login).ToListAsync(ct);
        return users.Select(ToDto).ToList();
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        var entity = new UserEntity { Login = request.Login.Trim() };
        entity.PasswordHash = _hasher.HashPassword(entity, request.Password);
        _db.Users.Add(entity);
        await _db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<bool> ChangePasswordAsync(string id, string newPassword, CancellationToken ct = default)
    {
        var entity = await _db.Users.FindAsync([id], ct);
        if (entity is null) return false;
        entity.PasswordHash = _hasher.HashPassword(entity, newPassword);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> SetActiveAsync(string id, bool isActive, CancellationToken ct = default)
    {
        var entity = await _db.Users.FindAsync([id], ct);
        if (entity is null) return false;
        entity.IsActive = isActive;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        var entity = await _db.Users.FindAsync([id], ct);
        if (entity is null) return false;
        _db.Users.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<UserPrincipal?> ValidateAsync(string login, string password, CancellationToken ct = default)
    {
        var entity = await _db.Users.FirstOrDefaultAsync(u => u.Login == login, ct);
        if (entity is null)
        {
            _hasher.VerifyHashedPassword(new UserEntity(), DummyHash.Value, password);
            return null;
        }
        if (!entity.IsActive) return null;
        var result = _hasher.VerifyHashedPassword(entity, entity.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed) return null;
        return ToPrincipal(entity);
    }

    public Task<bool> ExistsAsync(string login, CancellationToken ct = default)
        => _db.Users.AnyAsync(u => u.Login == login, ct);

    public Task<bool> AnyAsync(CancellationToken ct = default)
        => _db.Users.AnyAsync(ct);

    private static UserDto ToDto(UserEntity e) => new()
    {
        Id = e.Id,
        Login = e.Login,
        CreatedAt = e.CreatedAt,
        IsActive = e.IsActive,
    };

    private static UserPrincipal ToPrincipal(UserEntity e) => new()
    {
        Id = e.Id,
        Login = e.Login,
    };
}
