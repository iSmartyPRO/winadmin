using Microsoft.EntityFrameworkCore;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Infrastructure.Access;

/// <summary>Старые scopes → роли: admin → «Администратор», остальное → «Импорт: …» (одна роль на набор).</summary>
public static class LegacyScopes
{
    public static async Task AssignAsync(WinAdminDbContext db, PermissionCatalog catalog, PrincipalType type,
        string principalId, string displayName, IEnumerable<string> scopes, string createdBy, CancellationToken ct = default)
    {
        var list = scopes.Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        string roleId;
        if (list.Contains("admin", StringComparer.OrdinalIgnoreCase))
        {
            roleId = BuiltInRoles.AdministratorId;
        }
        else
        {
            var permissions = list
                .Select(s => s == "disks.read" ? PermissionIds.SystemRead : s)
                .Where(s => catalog.Find(s) is not null)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();
            if (permissions.Count == 0)
                return;

            string name = "Импорт: " + string.Join(", ", permissions);
            var role = db.Roles.Local.FirstOrDefault(r => r.Name == name)
                       ?? await db.Roles.FirstOrDefaultAsync(r => r.Name == name, ct);
            if (role is null)
            {
                role = new RoleEntity { Name = name, Description = "Создана из прежних scopes при обновлении WinAdmin" };
                foreach (var p in permissions)
                    role.Permissions.Add(new RolePermissionEntity { PermissionId = p });
                db.Roles.Add(role);
            }
            roleId = role.Id;
        }

        string t = type.ToString();
        bool exists = db.RoleAssignments.Local.Any(a => a.RoleId == roleId && a.PrincipalType == t && a.PrincipalId == principalId)
                      || await db.RoleAssignments.AnyAsync(a => a.RoleId == roleId && a.PrincipalType == t && a.PrincipalId == principalId, ct);
        if (!exists)
        {
            db.RoleAssignments.Add(new RoleAssignmentEntity
            {
                RoleId = roleId, PrincipalType = t, PrincipalId = principalId, DisplayName = displayName, CreatedBy = createdBy,
            });
        }
    }
}
