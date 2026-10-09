using Microsoft.Extensions.Logging;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.ActiveDirectory.Users;
using WinAdmin.Core.Models;
using WinAdmin.Core.Security;

namespace WinAdmin.Infrastructure.ActiveDirectory.Users;

/// <summary>
/// Пользователи AD. Управляемый пользователь — в проекте под корневой OU (не скрытом) и в области оператора;
/// уволенный — в OU уволенных: виден и доступен тем, у кого есть ad-users.offboard.
/// </summary>
public sealed partial class AdUsersService(
    IAdUserDirectory directory, IAdWriter writer, IAdReader reader, IAdStructureStore structure,
    IModuleRegistry modules, IAuditService audit, ILogger<AdUsersService> logger) : IAdUsersService
{
    private sealed record Located(AdUser User, string? ProjectDn, bool Terminated);

    public async Task<IReadOnlyList<AdUserView>> ListAsync(IAccessContext actor, string? projectDn, AdUserStatus status, string? q, CancellationToken ct = default)
    {
        var (st, settings) = await ConfigAsync(ct);
        var attributes = Attributes(settings);
        var projects = await reader.ListProjectsAsync(false, ct);
        IEnumerable<AdUser> users;

        if (status == AdUserStatus.Terminated)
        {
            if (!actor.Permissions.Has(PermissionIds.AdUsersOffboard))
                throw new AccessDeniedException("Уволенных видят только с правом увольнения.", [PermissionIds.AdUsersOffboard]);
            if (string.IsNullOrWhiteSpace(settings.TerminatedOuDn))
                throw new InvalidOperationException("Не задана OU уволенных (настройки модуля «Пользователи AD»).");
            users = await directory.ListUsersAsync(settings.TerminatedOuDn, attributes, ct);
        }
        else
        {
            var visible = AdGuard.InScopeProjects(actor, PermissionIds.AdUsersRead, projects);
            if (projectDn is not null)
            {
                var project = visible.FirstOrDefault(p => DnUtils.IsUnderOrSame(projectDn, p.Dn) && DnUtils.IsUnderOrSame(p.Dn, projectDn))
                              ?? throw new AccessDeniedException("Проект вне вашей области.", [PermissionIds.AdUsersRead]);
                visible = [project];
            }
            var list = new List<AdUser>();
            foreach (var p in visible) list.AddRange(await directory.ListUsersAsync(p.Dn, attributes, ct));
            users = list;
        }

        string? query = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        return users
            .Where(u => status switch { AdUserStatus.Active => u.Enabled, AdUserStatus.Disabled => !u.Enabled, _ => true })
            .Where(u => query is null || Matches(u, query))
            .Select(u => View(u, st, settings))
            .OrderBy(v => v.DisplayName ?? v.Sam, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<AdUserCard> GetAsync(IAccessContext actor, string sam, CancellationToken ct = default)
    {
        var (st, settings) = await ConfigAsync(ct);
        var located = await LocateAsync(actor, sam, PermissionIds.AdUsersRead, st, settings, ct);
        var groups = await directory.GetGroupsAsync(located.User, ct);
        return new AdUserCard(View(located.User, st, settings), groups);
    }

    public async Task<byte[]?> GetPhotoAsync(IAccessContext actor, string sam, CancellationToken ct = default)
    {
        var (st, settings) = await ConfigAsync(ct);
        var located = await LocateAsync(actor, sam, PermissionIds.AdUsersRead, st, settings, ct);
        return located.User.HasPhoto ? await directory.GetPhotoAsync(located.User.Dn, ct) : null;
    }

    public async Task<IReadOnlyList<AuditEntryDto>> HistoryAsync(IAccessContext actor, string sam, CancellationToken ct = default)
    {
        var (st, settings) = await ConfigAsync(ct);
        var located = await LocateAsync(actor, sam, PermissionIds.AdUsersRead, st, settings, ct);
        return await audit.QueryAsync(200, null, located.User.Dn, ct);
    }

    // ── общие помощники ──────────────────────────────────────────

    private async Task<(AdStructureSettings, AdUsersSettings)> ConfigAsync(CancellationToken ct)
    {
        var st = await structure.GetAsync(ct);
        if (st.RootOu is null) throw new InvalidOperationException("Корневая OU не задана (Настройки → Active Directory).");
        return (st, await modules.GetSettingsAsync<AdUsersSettings>(AdUsersModule.ModuleId, ct));
    }

    private static IReadOnlyCollection<string> Attributes(AdUsersSettings settings)
        => AdUsersSettings.DefaultAttributes.Concat(settings.EditableAttributes).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Найти по логину и проверить: уволенный — нужен offboard; иначе зона + область для permission.</summary>
    private async Task<Located> LocateAsync(IAccessContext actor, string sam, string permission,
        AdStructureSettings st, AdUsersSettings settings, CancellationToken ct)
    {
        var user = await directory.FindUserAsync(sam, Attributes(settings), ct)
                   ?? throw new KeyNotFoundException($"Пользователь «{sam}» не найден в AD.");
        if (IsTerminated(user, settings))
        {
            if (!actor.Permissions.Has(PermissionIds.AdUsersOffboard))
                throw new AccessDeniedException("Пользователь уволен: нужен доступ к увольнению.", [PermissionIds.AdUsersOffboard]);
            return new Located(user, null, true);
        }
        string project = AdGuard.EnsureManaged(user.Dn, st);
        AdGuard.EnsureInScope(actor, permission, project);
        return new Located(user, project, false);
    }

    private static bool IsTerminated(AdUser user, AdUsersSettings settings)
        => !string.IsNullOrWhiteSpace(settings.TerminatedOuDn) && DnUtils.IsUnderOrSame(user.Dn, settings.TerminatedOuDn);

    private static bool Matches(AdUser u, string q)
        => u.Sam.Contains(q, StringComparison.OrdinalIgnoreCase)
           || (u.DisplayName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
           || (u.Attr("mail")?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);

    private static AdUserView View(AdUser u, AdStructureSettings st, AdUsersSettings settings)
    {
        bool terminated = IsTerminated(u, settings);
        string? project = terminated ? null : DnUtils.ProjectDn(u.Dn, st.RootOu!);
        return new AdUserView(u.Sam, u.Dn, u.DisplayName, u.Enabled, u.LastLogon, u.WhenCreated, u.HasPhoto,
            project, project is null ? null : DnUtils.RelativeOuPath(project, st.RootOu!)[0], terminated, u.Attributes);
    }

    private Task AuditAsync(IAccessContext actor, string action, string target, bool success, string details, CancellationToken ct)
        => audit.WriteAsync(new AuditEntryDto { Actor = actor.Actor, Action = action, Target = target, Success = success, Details = details }, ct);
}
