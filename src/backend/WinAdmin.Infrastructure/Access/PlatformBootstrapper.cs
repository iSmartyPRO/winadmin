using Microsoft.EntityFrameworkCore;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Infrastructure.Access;

/// <summary>
/// При каждом старте: встроенная роль «Администратор» и перенос непустых старых scopes
/// пользователей и ключей в роли (после переноса колонка Scopes очищается — повтор ничего не делает).
/// </summary>
public static class PlatformBootstrapper
{
    public static async Task RunAsync(WinAdminDbContext db, PermissionCatalog catalog, CancellationToken ct = default)
    {
        if (!await db.Roles.AnyAsync(r => r.Id == BuiltInRoles.AdministratorId, ct))
        {
            db.Roles.Add(new RoleEntity
            {
                Id = BuiltInRoles.AdministratorId,
                Name = BuiltInRoles.AdministratorName,
                Description = "Все права, включая права будущих модулей",
                IsBuiltin = true,
            });
            await db.SaveChangesAsync(ct);
        }

        foreach (var user in await db.Users.Where(u => u.Scopes != "").ToListAsync(ct))
        {
            await LegacyScopes.AssignAsync(db, catalog, PrincipalType.LocalUser, user.Id, user.Login,
                user.Scopes.Split(','), "migration", ct);
            user.Scopes = "";
        }
        foreach (var key in await db.ApiKeys.Where(k => k.Scopes != "").ToListAsync(ct))
        {
            await LegacyScopes.AssignAsync(db, catalog, PrincipalType.ApiKey, key.Id, key.Name,
                key.Scopes.Split(','), "migration", ct);
            key.Scopes = "";
        }
        await db.SaveChangesAsync(ct);
    }
}
