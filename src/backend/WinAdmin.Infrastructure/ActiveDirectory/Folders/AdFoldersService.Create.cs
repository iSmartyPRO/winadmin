using Microsoft.Extensions.Logging;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.ActiveDirectory.Folders;
using WinAdmin.Core.Models;
using WinAdmin.Core.Operations;
using WinAdmin.Core.Security;

namespace WinAdmin.Infrastructure.ActiveDirectory.Folders;

public sealed partial class AdFoldersService
{
    public async Task<FolderPreview> PreviewAsync(IAccessContext actor, CreateFolderRequest request, CancellationToken ct = default)
    {
        var (st, settings) = await ConfigAsync(ct);
        return await PlanAsync(actor, request, st, settings, ct);
    }

    public async Task<IReadOnlyList<ScenarioStep>> CreateFolderAsync(IAccessContext actor, CreateFolderRequest request, CancellationToken ct = default)
    {
        var (st, settings) = await ConfigAsync(ct);
        var plan = await PlanAsync(actor, request, st, settings, ct);
        string path = request.Path.Trim().TrimEnd('\\');
        var run = new ScenarioRunner(ex => logger.LogError(ex, "Новая папка {Path}", path));
        string? fullSid = null, readSid = null;

        await run.RunAsync($"Группа Full «{plan.FullGroup}»", async () =>
        {
            var (sid, result) = await EnsureGroupAsync(plan.FullGroup, plan.GroupsOuDn, $"{path};Full Access", path, ct);
            fullSid = sid;
            return result;
        });
        await run.RunAsync($"Группа Read «{plan.ReadGroup}»", async () =>
        {
            var (sid, result) = await EnsureGroupAsync(plan.ReadGroup, plan.GroupsOuDn, $"{path};Read Only", path, ct);
            readSid = sid;
            return result;
        });
        await run.RunAsync($"Папка {plan.Unc}", async () =>
        {
            if (await ntfs.DirectoryExistsAsync(plan.Unc, ct)) return StepResult.Skip("уже есть");
            if (!request.CreateDirectory) throw new InvalidOperationException("Папки нет на файловом сервере — включите «создать папку».");
            await ntfs.CreateDirectoryAsync(plan.Unc, ct);
            return StepResult.Done();
        });
        await run.RunAsync("Права NTFS", async () =>
        {
            int added = await ntfs.GrantAsync(plan.Unc, Needs(fullSid!, readSid!, settings), ct);
            return added > 0 ? StepResult.Done($"добавлено правил: {added}") : StepResult.Skip("права уже настроены");
        });

        cache.Invalidate();
        await audit.WriteAsync(new AuditEntryDto
        {
            Actor = actor.Actor, Action = "folder.create", Target = plan.Unc, Success = !run.Failed,
            Details = $"{path}; группы {plan.FullGroup}, {plan.ReadGroup}; " + string.Join("; ", run.Steps.Select(s => $"{s.Name}: {s.Status}")),
        }, ct);
        return run.Steps;
    }

    public async Task<FolderAclState> InspectAclAsync(IAccessContext actor, string path, CancellationToken ct = default)
    {
        var (st, settings) = await ConfigAsync(ct);
        var (folder, unc, needs) = await FolderForAclAsync(actor, path, PermissionIds.AdFoldersRead, st, settings, ct);
        return await ntfs.InspectAsync(unc, needs, ct);
    }

    public async Task<IReadOnlyList<ScenarioStep>> FixAclAsync(IAccessContext actor, string path, CancellationToken ct = default)
    {
        var (st, settings) = await ConfigAsync(ct);
        var (folder, unc, needs) = await FolderForAclAsync(actor, path, PermissionIds.AdFoldersCreate, st, settings, ct);
        var run = new ScenarioRunner(ex => logger.LogError(ex, "Права NTFS {Unc}", unc));
        await run.RunAsync("Права NTFS", async () =>
        {
            int added = await ntfs.GrantAsync(unc, needs, ct);
            return added > 0 ? StepResult.Done($"добавлено правил: {added}") : StepResult.Skip("права уже настроены");
        });
        await audit.WriteAsync(new AuditEntryDto
        {
            Actor = actor.Actor, Action = "folder.acl.fix", Target = unc, Success = !run.Failed,
            Details = $"{folder.Path}: {run.Steps[0].Status} {run.Steps[0].Message}",
        }, ct);
        return run.Steps;
    }

    /// <summary>Проверки и имена — до любых изменений.</summary>
    private async Task<FolderPreview> PlanAsync(IAccessContext actor, CreateFolderRequest request, AdStructureSettings st,
        AdFoldersSettings settings, CancellationToken ct)
    {
        string project = AdGuard.EnsureManaged(request.ProjectDn, st);
        AdGuard.EnsureInScope(actor, PermissionIds.AdFoldersCreate, project);
        string unc = FolderNaming.ToUnc(request.Path, Mappings(settings));
        string projectName = DnUtils.RelativeOuPath(project, st.RootOu!)[0];
        string? org = string.IsNullOrWhiteSpace(request.OrgCode) ? null : request.OrgCode.Trim();
        if (org is null)
        {
            var names = (await folders.ListGroupsAsync(project, settings.GroupPrefix, ct)).Select(g => g.Name);
            org = FolderNaming.DefaultOrgCode(projectName, names, settings.GroupPrefix)
                  ?? throw new ArgumentException("Укажите код организации латиницей (по имени проекта его не получить).");
        }
        var (full, read) = FolderNaming.GroupNames(settings.GroupPrefix, org, request.BaseName.Trim());
        string groupsOu = string.IsNullOrWhiteSpace(settings.GroupsOuName) ? project : $"OU={settings.GroupsOuName.Trim()},{project}";
        NtfsAccess.ParseRights(settings.FullRights);
        NtfsAccess.ParseRights(settings.ReadRights);
        return new FolderPreview(full, read, unc, groupsOu);
    }

    /// <summary>Группа есть (с тем же путём) — пропуск; есть с другим путём или вне OU — ошибка; нет — создать.</summary>
    private async Task<(string Sid, StepResult Result)> EnsureGroupAsync(string name, string ouDn, string description, string path, CancellationToken ct)
    {
        var existing = await folders.FindGroupByNameAsync(name, ct);
        if (existing is not null)
        {
            var parsed = FolderDescriptionParser.Parse(existing.Description, existing.Info, existing.Name);
            if (parsed is null || FolderDescriptionParser.NormalizeKey(parsed.Path) != FolderDescriptionParser.NormalizeKey(path))
                throw new InvalidOperationException($"Имя «{name}» занято другой папкой: {parsed?.Path ?? existing.Dn}.");
            return (existing.Sid ?? throw new InvalidOperationException($"У группы «{name}» нет SID."), StepResult.Skip("уже есть"));
        }
        string dn = await writer.CreateGroupAsync(ouDn, name, description, ct);
        var created = await folders.GetGroupAsync(dn, ct) ?? throw new InvalidOperationException($"Группа «{name}» создана, но не читается.");
        return (created.Sid!, StepResult.Done());
    }

    private async Task<(Folder Folder, string Unc, IReadOnlyList<AclNeed> Needs)> FolderForAclAsync(IAccessContext actor, string path,
        string permission, AdStructureSettings st, AdFoldersSettings settings, CancellationToken ct)
    {
        var catalog = await CatalogAsync(st, settings, ct);
        var folder = catalog.Folders.FirstOrDefault(f => FolderDescriptionParser.NormalizeKey(f.Path) == FolderDescriptionParser.NormalizeKey(path))
                     ?? throw new KeyNotFoundException($"Папка «{path}» не найдена среди групп доступа.");
        AdGuard.EnsureInScope(actor, permission, folder.ProjectDn ?? throw new AccessDeniedException("Папка вне проектов.", [permission]));
        if (folder.Full?.Sid is null || folder.Read?.Sid is null)
            throw new InvalidOperationException("У папки нет пары групп Full/Read — права NTFS не настроить.");
        string unc = FolderNaming.ToUnc(folder.Path, Mappings(settings));
        return (folder, unc, Needs(folder.Full.Sid, folder.Read.Sid, settings));
    }

    private static IReadOnlyList<AclNeed> Needs(string fullSid, string readSid, AdFoldersSettings settings)
        => [new AclNeed(fullSid, "Full", settings.FullRights), new AclNeed(readSid, "Read", settings.ReadRights)];
}
