using WinAdmin.Core.Security;

namespace WinAdmin.Core.ActiveDirectory;

/// <summary>Охрана записи: объект в зоне управления и проект в области действующего лица.</summary>
public static class AdGuard
{
    /// <summary>Возвращает DN проекта объекта; вне RootOu, вне проекта или в скрытой OU — 403.</summary>
    public static string EnsureManaged(string dn, AdStructureSettings settings)
    {
        if (settings.RootOu is null)
            throw new InvalidOperationException("Корневая OU не задана (Настройки → Active Directory).");
        var project = DnUtils.ProjectDn(dn, settings.RootOu)
                      ?? throw new AccessDeniedException("Объект вне управляемых проектов AD.", []);
        string name = DnUtils.RelativeOuPath(project, settings.RootOu)[0];
        if (settings.HiddenOus.Contains(name, StringComparer.OrdinalIgnoreCase))
            throw new AccessDeniedException($"Проект «{name}» скрыт настройками.", []);
        return project;
    }

    public static void EnsureInScope(IAccessContext actor, string permissionId, string projectDn)
    {
        var scope = actor.Permissions.ScopeFor(permissionId)
                    ?? throw new AccessDeniedException("Недостаточно прав.", [permissionId]);
        if (scope.IsUnrestricted) return;
        if (!scope.Items!.Any(p => DnUtils.IsUnderOrSame(projectDn, p)))
            throw new AccessDeniedException("Проект вне вашей области.", [permissionId]);
    }

    public static IReadOnlyList<AdProject> InScopeProjects(IAccessContext actor, string permissionId, IEnumerable<AdProject> projects)
    {
        var scope = actor.Permissions.ScopeFor(permissionId);
        if (scope is null) return [];
        return scope.IsUnrestricted
            ? projects.ToList()
            : projects.Where(p => scope.Items!.Any(s => DnUtils.IsUnderOrSame(p.Dn, s))).ToList();
    }
}
