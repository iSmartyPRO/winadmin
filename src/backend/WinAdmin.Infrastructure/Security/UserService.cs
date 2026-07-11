using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Infrastructure.Security;

public sealed class UserService : IUserService
{
    private readonly WinAdminDbContext _db;
    private readonly PasswordHasher<UserEntity> _hasher = new();

    public UserService(WinAdminDbContext db) => _db = db;

    public async Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken ct = default)
    {
        var users = await _db.Users.AsNoTracking().OrderBy(u => u.Login).ToListAsync(ct);
        return users.Select(ToDto).ToList();
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        var scopes = NormalizeScopes(request.Scopes);
        var entity = new UserEntity
        {
            Login = request.Login.Trim(),
            Scopes = string.Join(',', scopes),
        };
        entity.PasswordHash = _hasher.HashPassword(entity, request.Password);
        _db.Users.Add(entity);
        await _db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<bool> UpdateScopesAsync(string id, IEnumerable<string> scopes, CancellationToken ct = default)
    {
        var entity = await _db.Users.FindAsync([id], ct);
        if (entity is null) return false;
        entity.Scopes = string.Join(',', NormalizeScopes(scopes));
        await _db.SaveChangesAsync(ct);
        return true;
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
        if (entity is null || !entity.IsActive) return null;
        var result = _hasher.VerifyHashedPassword(entity, entity.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed) return null;
        return ToPrincipal(entity);
    }

    public Task<bool> AnyAsync(CancellationToken ct = default)
        => _db.Users.AnyAsync(ct);

    private static List<string> NormalizeScopes(IEnumerable<string> scopes)
        => scopes.Select(s => s.Trim()).Where(Scopes.IsValid).Distinct().ToList();

    private static UserDto ToDto(UserEntity e) => new()
    {
        Id = e.Id,
        Login = e.Login,
        Scopes = e.Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        CreatedAt = e.CreatedAt,
        IsActive = e.IsActive,
    };

    private static UserPrincipal ToPrincipal(UserEntity e) => new()
    {
        Id = e.Id,
        Login = e.Login,
        Scopes = e.Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
    };
}
