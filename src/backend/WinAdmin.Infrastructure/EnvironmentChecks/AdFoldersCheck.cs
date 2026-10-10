using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.ActiveDirectory.Folders;
using WinAdmin.Core.EnvironmentChecks;

namespace WinAdmin.Infrastructure.EnvironmentChecks;

/// <summary>Готовность модуля «Папки»: группы, сопоставление дисков, права на группы и создание, шара, ACL папок (полная).</summary>
public sealed class AdFoldersCheck(
    IAdReader reader, IAdFolderDirectory folders, IAdUserDirectory users, INtfsAccess ntfs,
    IAdStructureStore structure, IModuleRegistry modules) : IEnvironmentCheck
{
    private const int MaxFolders = 500;
    private const int MaxListed = 15;

    public string ModuleId => AdFoldersModule.ModuleId;

    public async Task<IReadOnlyList<CheckResult>> RunAsync(CheckDepth depth, CancellationToken ct)
    {
        var st = await structure.GetAsync(ct);
        string[] codes = ["folders.groups", "folders.mappings", "ad.rights.groups", "ad.rights.create", "folders.share"];
        if (st.RootOu is null)
            return codes.Select(c => CheckResult.Skip(c, Title(c), "Корневая OU не задана (см. «Платформа»)")).ToList();

        var settings = await modules.GetSettingsAsync<AdFoldersSettings>(AdFoldersModule.ModuleId, ct);
        var results = new List<CheckResult>();
        var projects = await reader.ListProjectsAsync(false, ct);
        var groups = (await folders.ListGroupsAsync(st.RootOu, settings.GroupPrefix, ct))
            .Where(g => DnUtils.ProjectDn(g.Dn, st.RootOu) is { } p && projects.Any(x => DnUtils.IsUnderOrSame(p, x.Dn)))
            .ToList();

        IReadOnlyDictionary<char, string>? map = null;
        string? mapError = null;
        try { map = FolderNaming.ParseMappings(settings.DriveMappings); }
        catch (ArgumentException ex) { mapError = ex.Message; }

        var catalog = FolderCatalog.Build(groups, new Dictionary<string, AdMember>(), st.RootOu, map?.Keys.ToList() ?? []);

        // 1. Группы.
        var noPair = catalog.Folders.Where(f => f.Full is null || f.Read is null).Select(f => f.Path).ToList();
        var dupes = catalog.Folders.Where(f => f.Warnings.Any(w => w.StartsWith("две группы"))).Select(f => f.Path).ToList();
        string summary = $"Групп: {groups.Count}, папок: {catalog.Folders.Count}";
        var problems = new List<string>();
        if (catalog.Unparsed.Count > 0) problems.Add("без пути в описании: " + List(catalog.Unparsed.Select(u => u.Name)));
        if (noPair.Count > 0) problems.Add("без пары Full/Read: " + List(noPair));
        if (dupes.Count > 0) problems.Add("дубли групп: " + List(dupes));
        results.Add(problems.Count == 0
            ? CheckResult.Ok("folders.groups", Title("folders.groups"), summary)
            : CheckResult.Warn("folders.groups", Title("folders.groups"), summary + "; " + string.Join("; ", problems),
                $"Исправьте описание групп: «путь;Full Access» / «путь;Read Only»"));

        // 2. Сопоставление дисков.
        var drives = catalog.Folders.Where(f => f.Path.Length > 1 && f.Path[1] == ':').Select(f => char.ToUpperInvariant(f.Path[0])).Distinct().ToList();
        if (mapError is not null)
            results.Add(CheckResult.Fail("folders.mappings", Title("folders.mappings"), mapError, @"Модули → «Папки» → «Буквы дисков → UNC»: строки вида A=\\fs01\Projects"));
        else
        {
            var unmapped = drives.Where(d => !map!.ContainsKey(d)).ToList();
            results.Add(unmapped.Count == 0
                ? CheckResult.Ok("folders.mappings", Title("folders.mappings"), drives.Count == 0 ? "Букв дисков в описаниях нет" : "Сопоставлены: " + string.Join(", ", drives.Select(d => $"{d}:")))
                : CheckResult.Fail("folders.mappings", Title("folders.mappings"), "Не сопоставлены: " + string.Join(", ", unmapped.Select(d => $"{d}:")),
                    @"Модули → «Папки» → «Буквы дисков → UNC»: добавьте строки вида A=\\fs01\Projects"));
        }

        // 3–4. Права учётки записи на группы и создание групп.
        var writer = await reader.GetWriterStatusAsync(ct);
        if (!writer.Bound)
        {
            results.Add(CheckResult.Skip("ad.rights.groups", Title("ad.rights.groups"), "Учётка записи не вошла (см. «Платформа»)"));
            results.Add(CheckResult.Skip("ad.rights.create", Title("ad.rights.create"), "Учётка записи не вошла (см. «Платформа»)"));
        }
        else
        {
            var noMember = new List<string>();
            foreach (var p in projects)
            {
                var sample = groups.FirstOrDefault(g => DnUtils.IsUnderOrSame(g.Dn, p.Dn));
                if (sample is not null && !(await reader.ReadEffectiveAsync(sample.Dn, ct)).Attributes.Contains("member")) noMember.Add(p.Name);
            }
            results.Add(noMember.Count == 0
                ? CheckResult.Ok("ad.rights.groups", Title("ad.rights.groups"), "Изменение участников групп разрешено")
                : CheckResult.Fail("ad.rights.groups", Title("ad.rights.groups"), "Нет права менять участников групп в проектах: " + List(noMember),
                    $"Делегируйте {writer.Account} «Write members» на группы {settings.GroupPrefix}* в этих проектах"));

            var noCreate = new List<string>();
            foreach (var p in projects)
            {
                string ou = string.IsNullOrWhiteSpace(settings.GroupsOuName) ? p.Dn : $"OU={settings.GroupsOuName.Trim()},{p.Dn}";
                if (!await reader.ExistsAsync(ou, ct) || !(await reader.ReadEffectiveAsync(ou, ct)).ChildClasses.Contains("group")) noCreate.Add(p.Name);
            }
            results.Add(noCreate.Count == 0
                ? CheckResult.Ok("ad.rights.create", Title("ad.rights.create"), "Создание групп разрешено")
                : CheckResult.Fail("ad.rights.create", Title("ad.rights.create"), "Нельзя создавать группы в проектах: " + List(noCreate),
                    $"Делегируйте {writer.Account} «Create Group objects» на OU групп этих проектов"));
        }

        // 5. Шары.
        if (map is null || map.Count == 0)
            results.Add(CheckResult.Skip("folders.share", Title("folders.share"), "Сопоставления дисков не заданы"));
        else
        {
            var sids = writer.Bound ? await users.GetWriterSidsAsync(ct) : [];
            var missing = new List<string>();
            var unconfirmed = new List<string>();
            foreach (var unc in map.Values)
            {
                try
                {
                    if (!await ntfs.DirectoryExistsAsync(unc, ct)) { missing.Add(unc); continue; }
                    if (!await ntfs.HasExplicitChangePermissionsAsync(unc, sids, ct)) unconfirmed.Add(unc);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    missing.Add($"{unc} ({ex.Message})");
                }
            }
            results.Add(missing.Count > 0
                ? CheckResult.Fail("folders.share", Title("folders.share"), "Недоступны: " + string.Join("; ", missing),
                    "Проверьте путь, доступ по SMB (445) и права учётки записи на шару")
                : unconfirmed.Count > 0
                    ? CheckResult.Warn("folders.share", Title("folders.share"), "Не подтверждено явное право «Изменение разрешений»: " + string.Join("; ", unconfirmed),
                        $"Дайте {writer.Account} право «Изменение разрешений» (или «Полный доступ») на корне шары; права через локальные группы сервера здесь не видны")
                    : CheckResult.Ok("folders.share", Title("folders.share"), "Шары доступны: " + string.Join("; ", map.Values)));
        }

        // 6. ACL папок — только полная проверка.
        if (depth == CheckDepth.Full)
            results.Add(await AclScanAsync(catalog, map, settings, ct));
        return results;
    }

    private async Task<CheckResult> AclScanAsync(FolderCatalogResult catalog, IReadOnlyDictionary<char, string>? map, AdFoldersSettings settings, CancellationToken ct)
    {
        if (map is null) return CheckResult.Skip("folders.acl", Title("folders.acl"), "Сопоставления дисков не заданы");
        var bad = new List<string>();
        int checkedCount = 0;
        foreach (var f in catalog.Folders.Where(f => f.Full?.Sid is not null && f.Read?.Sid is not null).Take(MaxFolders))
        {
            string unc;
            try { unc = FolderNaming.ToUnc(f.Path, map); }
            catch (ArgumentException) { continue; }
            checkedCount++;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                var state = await ntfs.InspectAsync(unc, [new AclNeed(f.Full!.Sid!, "Full", settings.FullRights), new AclNeed(f.Read!.Sid!, "Read", settings.ReadRights)], timeout.Token);
                if (!state.Ok) bad.Add($"{f.Path} ({string.Join(", ", state.Missing)})");
            }
            catch (Exception ex) when (ex is OperationCanceledException or UnauthorizedAccessException or IOException)
            {
                bad.Add($"{f.Path} (не прочитать: {ex.Message})");
            }
        }
        return bad.Count == 0
            ? CheckResult.Ok("folders.acl", Title("folders.acl"), $"Права в порядке у {checkedCount} папок")
            : CheckResult.Fail("folders.acl", Title("folders.acl"), $"Расхождения ({bad.Count} из {checkedCount}): " + List(bad),
                "Откройте папку в разделе «Папки» → «Исправить права NTFS»");
    }

    private static string List(IEnumerable<string> items)
    {
        var list = items.ToList();
        return string.Join(", ", list.Take(MaxListed)) + (list.Count > MaxListed ? $" и ещё {list.Count - MaxListed}" : "");
    }

    private static string Title(string code) => code switch
    {
        "folders.groups" => "Группы доступа к папкам",
        "folders.mappings" => "Сопоставление дисков",
        "ad.rights.groups" => "Права на участников групп",
        "ad.rights.create" => "Права на создание групп",
        "folders.share" => "Файловый сервер",
        "folders.acl" => "Права NTFS на папках",
        _ => code,
    };
}
