using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory.Folders;
using WinAdmin.Core.ActiveDirectory.Users;
using WinAdmin.Core.Models;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;

namespace WinAdmin.Core.ActiveDirectory;

/// <summary>Роли-шаблоны модулей AD: создаются при первом включении модуля и при импорте из Access.</summary>
public static class AdRoleTemplates
{
    public static IReadOnlyList<SaveRoleRequest> Users { get; } =
    [
        new(AdUsersModule.HrRoleName, "Шаблон: просмотр, правка атрибутов и перенос пользователей AD",
            [new(PermissionIds.AdUsersRead, null), new(PermissionIds.AdUsersEdit, null), new(PermissionIds.AdUsersMove, null)]),
        new(AdUsersModule.AdminRoleName, "Шаблон: все действия с пользователями AD",
            [new(PermissionIds.AdUsersRead, null), new(PermissionIds.AdUsersEdit, null), new(PermissionIds.AdUsersMove, null),
             new(PermissionIds.AdUsersPassword, null), new(PermissionIds.AdUsersOffboard, null)]),
    ];

    public static IReadOnlyList<SaveRoleRequest> Folders { get; } =
    [
        new(AdFoldersModule.AdminRoleName, "Шаблон: все действия с папками",
            [new(PermissionIds.AdFoldersRead, null), new(PermissionIds.AdFoldersMembership, null), new(PermissionIds.AdFoldersCreate, null)]),
        // Права роли admin/super_admin портала Access: просмотр папок и участники — без создания папок и прав NTFS.
        new(AdFoldersModule.MembersRoleName, "Шаблон: просмотр папок и участники Full/Read (без создания папок)",
            [new(PermissionIds.AdFoldersRead, null), new(PermissionIds.AdFoldersMembership, null)]),
    ];

    public static IReadOnlyList<SaveRoleRequest> All { get; } = [.. Users, .. Folders];

    /// <summary>Служебное действующее лицо с правами администратора (для создания ролей и назначений без входа).</summary>
    public static IAccessContext SystemContext(PermissionCatalog catalog, string actor = "system")
        => new AccessContext(new PrincipalRef(PrincipalType.LocalUser, actor, []), actor,
            PermissionEvaluator.Evaluate([new RoleSnapshot(BuiltInRoles.AdministratorId, true, [])], catalog));

    /// <summary>Создаёт шаблоны, которых ещё нет (по имени без учёта регистра); существующие не трогает. Возвращает созданные имена.</summary>
    public static async Task<IReadOnlyList<string>> EnsureAsync(
        IRoleService roles, PermissionCatalog catalog, IEnumerable<SaveRoleRequest> templates, CancellationToken ct = default)
    {
        var existing = (await roles.ListAsync(ct)).Select(r => r.Name).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        var system = SystemContext(catalog);
        var created = new List<string>();
        foreach (var template in templates)
        {
            if (!existing.Add(template.Name)) continue;
            await roles.CreateAsync(template with { Permissions = [.. template.Permissions] }, system, ct);
            created.Add(template.Name);
        }
        return created;
    }
}
