using Microsoft.Extensions.Logging;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.ActiveDirectory.Folders;
using WinAdmin.Core.Models;
using WinAdmin.Core.Operations;
using WinAdmin.Core.Security;

namespace WinAdmin.Infrastructure.ActiveDirectory.Folders;

/// <summary>Папки: каталог из групп с префиксом в управляемых проектах, участники Full/Read, мастер, NTFS.</summary>
public sealed partial class AdFoldersService(
    IAdFolderDirectory folders, IAdUserDirectory users, IAdWriter writer, IAdReader reader, INtfsAccess ntfs,
    IAdStructureStore structure, IModuleRegistry modules, FolderCatalogCache cache, IAuditService audit,
    ILogger<AdFoldersService> logger) : IAdFoldersService
{
    public async Task<FolderCatalogResult> ListAsync(IAccessContext actor, string? projectDn, string? q, CancellationToken ct = default)
    {
        var (st, settings) = await ConfigAsync(ct);
        var catalog = await CatalogAsync(st, settings, ct);
        var projects = AdGuard.InScopeProjects(actor, PermissionIds.AdFoldersRead, await reader.ListProjectsAsync(false, ct));
        if (projectDn is not null)
        {
            var only = projects.FirstOrDefault(p => SameDn(p.Dn, projectDn))
                       ?? throw new AccessDeniedException("Проект вне вашей области.", [PermissionIds.AdFoldersRead]);
            projects = [only];
        }
        bool Visible(string? dn) => dn is not null && projects.Any(p => SameDn(p.Dn, dn));
        var visible = catalog.Folders.Where(f => Visible(f.ProjectDn)).ToList();
        return new FolderCatalogResult(FolderCatalog.Search(visible, q),
            catalog.Unparsed.Where(u => projects.Any(p => DnUtils.IsUnderOrSame(u.Dn, p.Dn))).ToList());
    }

    public async Task<IReadOnlyList<UserFolderAccess>> UserAccessAsync(IAccessContext actor, string sam, CancellationToken ct = default)
    {
        var (st, settings) = await ConfigAsync(ct);
        var user = await users.FindUserAsync(sam, [], ct) ?? throw new KeyNotFoundException($"Пользователь «{sam}» не найден в AD.");
        var visible = (await ListAsync(actor, null, null, ct)).Folders;
        return FolderCatalog.UserAccess(visible, user.Dn);
    }

    public async Task<IReadOnlyList<ScenarioStep>> ChangeMembershipAsync(IAccessContext actor, MembershipRequest request, CancellationToken ct = default)
    {
        var (st, settings) = await ConfigAsync(ct);
        var group = await GuardGroupAsync(actor, request.GroupDn, PermissionIds.AdFoldersMembership, st, settings, ct);
        var member = await folders.FindMemberAsync(request.Member, ct)
                     ?? throw new KeyNotFoundException($"«{request.Member}» не найден в AD.");

        var catalog = await CatalogAsync(st, settings, ct);
        var folder = catalog.Folders.FirstOrDefault(f => f.Full?.Dn == group.Dn || f.Read?.Dn == group.Dn
                                                        || f.Others.Any(o => o.Dn == group.Dn));
        var run = new ScenarioRunner(ex => logger.LogError(ex, "Членство {Group}", group.Dn));

        await run.RunAsync($"{(request.Add ? "Добавление в" : "Удаление из")} «{group.Name}»", async () =>
        {
            bool changed = request.Add ? await writer.AddMemberAsync(group.Dn, member.Dn, ct) : await writer.RemoveMemberAsync(group.Dn, member.Dn, ct);
            await AuditMembershipAsync(actor, request.Add, group, member, folder, !changed, ct);
            return changed ? StepResult.Done() : StepResult.Skip(request.Add ? "уже участник" : "не был участником");
        });

        var opposite = folder is null ? null
            : SameDn(folder.Full?.Dn, group.Dn) ? folder.Read
            : SameDn(folder.Read?.Dn, group.Dn) ? folder.Full : null;
        if (request.Add && request.RemoveFromOther && opposite is not null
            && opposite.Members.Any(m => SameDn(m.Dn, member.Dn)))
        {
            await run.RunAsync($"Удаление из «{opposite.Name}»", async () =>
            {
                bool removed = await writer.RemoveMemberAsync(opposite.Dn, member.Dn, ct);
                var oppositeGroup = new AdFolderGroup(opposite.Dn, opposite.Name, opposite.Sid, null, null, true, []);
                await AuditMembershipAsync(actor, false, oppositeGroup, member, folder, !removed, ct);
                return removed ? StepResult.Done() : StepResult.Skip("не был участником");
            });
        }

        cache.Invalidate();
        return run.Steps;
    }

    // ── помощники ────────────────────────────────────────────────

    private async Task<(AdStructureSettings, AdFoldersSettings)> ConfigAsync(CancellationToken ct)
    {
        var st = await structure.GetAsync(ct);
        if (st.RootOu is null) throw new InvalidOperationException("Корневая OU не задана (Настройки → Active Directory).");
        return (st, await modules.GetSettingsAsync<AdFoldersSettings>(AdFoldersModule.ModuleId, ct));
    }

    private Task<FolderCatalogResult> CatalogAsync(AdStructureSettings st, AdFoldersSettings settings, CancellationToken ct)
        => cache.GetAsync(async () =>
        {
            var projects = await reader.ListProjectsAsync(false, ct);
            var groups = (await folders.ListGroupsAsync(st.RootOu!, settings.GroupPrefix, ct))
                .Where(g => DnUtils.ProjectDn(g.Dn, st.RootOu!) is { } p && projects.Any(x => SameDn(x.Dn, p)))
                .ToList();
            var members = await folders.ResolveMembersAsync(groups.SelectMany(g => g.MemberDns), ct);
            var drives = Mappings(settings).Keys.ToList();
            return FolderCatalog.Build(groups, members, st.RootOu!, drives);
        });

    /// <summary>Группа в зоне и области, с префиксом модуля и безопасности.</summary>
    private async Task<AdFolderGroup> GuardGroupAsync(IAccessContext actor, string groupDn, string permission,
        AdStructureSettings st, AdFoldersSettings settings, CancellationToken ct)
    {
        string project = AdGuard.EnsureManaged(groupDn, st);
        AdGuard.EnsureInScope(actor, permission, project);
        var group = await folders.GetGroupAsync(groupDn, ct) ?? throw new KeyNotFoundException("Группа не найдена в AD.");
        if (!group.Name.StartsWith(settings.GroupPrefix, StringComparison.OrdinalIgnoreCase))
            throw new AccessDeniedException($"Модуль «Папки» управляет только группами «{settings.GroupPrefix}*».", [permission]);
        if (!group.IsSecurity)
            throw new InvalidOperationException($"«{group.Name}» — не группа безопасности.");
        return group;
    }

    private static IReadOnlyDictionary<char, string> Mappings(AdFoldersSettings settings)
        => FolderNaming.ParseMappings(settings.DriveMappings);

    private Task AuditMembershipAsync(IAccessContext actor, bool add, AdFolderGroup group, AdMember member, Folder? folder,
        bool alreadyMember, CancellationToken ct)
        => audit.WriteAsync(new AuditEntryDto
        {
            Actor = actor.Actor,
            Action = add ? "group.member.add" : "group.member.remove",
            Target = group.Dn,
            Success = true,
            Details = $"участник: {member.Sam ?? member.Name}; группа: {group.Name}; папка: {folder?.Path ?? "—"}; проект: {folder?.ProjectName ?? "—"}"
                      + (alreadyMember ? (add ? "; уже был участником" : "; не был участником") : ""),
        }, ct);

    private static bool SameDn(string? a, string? b)
        => a is not null && b is not null && DnUtils.IsUnderOrSame(a, b) && DnUtils.IsUnderOrSame(b, a);
}
