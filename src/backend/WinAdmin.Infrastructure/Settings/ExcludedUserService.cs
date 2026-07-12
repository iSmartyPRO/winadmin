using Microsoft.EntityFrameworkCore;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Infrastructure.Settings;

/// <inheritdoc />
public sealed class ExcludedUserService : IExcludedUserService
{
    private readonly WinAdminDbContext _db;

    public ExcludedUserService(WinAdminDbContext db) => _db = db;

    public async Task<IReadOnlyList<ExcludedUserDto>> ListAsync(CancellationToken ct = default)
    {
        var items = await _db.ExcludedUsers
            .AsNoTracking()
            .OrderBy(x => x.UserName)
            .ToListAsync(ct);
        return items.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<string>> ListNamesAsync(CancellationToken ct = default)
        => await _db.ExcludedUsers.AsNoTracking().Select(x => x.UserName).ToListAsync(ct);

    public async Task<ExcludedUserDto> AddAsync(string userName, CancellationToken ct = default)
    {
        var name = userName.Trim();
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("Имя учётной записи не может быть пустым.", nameof(userName));

        var entity = new ExcludedUserEntity { UserName = name };
        _db.ExcludedUsers.Add(entity);
        await _db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<bool> RemoveAsync(string id, CancellationToken ct = default)
    {
        var entity = await _db.ExcludedUsers.FindAsync([id], ct);
        if (entity is null) return false;
        _db.ExcludedUsers.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static ExcludedUserDto ToDto(ExcludedUserEntity e) => new()
    {
        Id = e.Id,
        UserName = e.UserName,
        CreatedAt = e.CreatedAt,
    };
}
