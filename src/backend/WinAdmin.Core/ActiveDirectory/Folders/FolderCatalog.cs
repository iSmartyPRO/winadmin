namespace WinAdmin.Core.ActiveDirectory.Folders;

/// <summary>Порт buildFolderCatalog / smartFolderSearch / getUserFolderMembership (Access).</summary>
public static class FolderCatalog
{
    public static FolderCatalogResult Build(
        IEnumerable<AdFolderGroup> groups, IReadOnlyDictionary<string, AdMember> members, string rootOu, IReadOnlyCollection<char> mappedDrives)
    {
        var unparsed = new List<UnparsedGroup>();
        var byKey = new Dictionary<string, (string Path, string? ProjectDn, List<(AdFolderGroup G, ParsedFolder P)> Items)>();
        foreach (var g in groups)
        {
            string? project = DnUtils.ProjectDn(g.Dn, rootOu);
            var parsed = FolderDescriptionParser.Parse(g.Description, g.Info, g.Name);
            if (parsed is null)
            {
                unparsed.Add(new UnparsedGroup(g.Dn, g.Name, Name(project, rootOu), "в описании нет пути к папке"));
                continue;
            }
            string key = FolderDescriptionParser.NormalizeKey(parsed.Path);
            if (!byKey.TryGetValue(key, out var entry))
                byKey[key] = entry = (parsed.Path.TrimEnd('\\', '/'), project, []);
            entry.Items.Add((g, parsed));
        }

        var folders = byKey.Values.Select(e =>
        {
            var full = e.Items.Where(i => i.P.Access == FolderAccess.Full).ToList();
            var read = e.Items.Where(i => i.P.Access == FolderAccess.Read).ToList();
            var warnings = new List<string>();
            if (full.Count > 1) warnings.Add("две группы Full");
            if (read.Count > 1) warnings.Add("две группы Read");
            if (full.Count == 0) warnings.Add("нет группы Full");
            if (read.Count == 0) warnings.Add("нет группы Read");
            if (e.Path.Length > 1 && e.Path[1] == ':' && !mappedDrives.Contains(char.ToUpperInvariant(e.Path[0])))
                warnings.Add($"буква диска {char.ToUpperInvariant(e.Path[0])}: не сопоставлена");
            return new Folder(e.Path, e.ProjectDn, Name(e.ProjectDn, rootOu),
                full.Select(i => View(i.G, members)).FirstOrDefault(),
                read.Select(i => View(i.G, members)).FirstOrDefault(),
                e.Items.Where(i => i.P.Access == FolderAccess.Other).Select(i => View(i.G, members)).ToList(),
                warnings);
        })
        .OrderBy(f => f.ProjectName ?? "", StringComparer.CurrentCultureIgnoreCase)
        .ThenBy(f => f.Path, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

        return new FolderCatalogResult(folders, unparsed);
    }

    public static IReadOnlyList<Folder> Search(IReadOnlyList<Folder> folders, string? query)
    {
        string q = (query ?? "").Trim();
        if (q.Length == 0) return folders;
        bool isPath = q.Contains(@":\") || q.StartsWith(@"\\") || q.StartsWith('/');
        if (isPath)
        {
            string nq = FolderDescriptionParser.NormalizeKey(q);
            var exact = folders.Where(f => FolderDescriptionParser.NormalizeKey(f.Path) == nq);
            var partial = folders.Where(f =>
            {
                string np = FolderDescriptionParser.NormalizeKey(f.Path);
                return np != nq && (np.Contains(nq) || nq.Contains(np));
            });
            return exact.Concat(partial).ToList();
        }
        var tokens = q.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return folders.Where(f =>
        {
            string hay = string.Join(" ", f.ProjectName ?? "", f.Path, f.Path.Replace('\\', ' ').Replace('/', ' '),
                f.Full?.Name ?? "", f.Read?.Name ?? "", "full access read only").ToLowerInvariant();
            return tokens.All(hay.Contains);
        }).ToList();
    }

    public static IReadOnlyList<UserFolderAccess> UserAccess(IReadOnlyList<Folder> folders, string userDn)
    {
        bool Has(FolderGroupView? g) => g?.Members.Any(m => string.Equals(m.Dn, userDn, StringComparison.OrdinalIgnoreCase)) == true;
        return folders.Select(f => new UserFolderAccess(f.ProjectName, f.Path, Has(f.Full), Has(f.Read)))
            .Where(a => a.HasFull || a.HasRead)
            .ToList();
    }

    private static FolderGroupView View(AdFolderGroup g, IReadOnlyDictionary<string, AdMember> members)
        => new(g.Dn, g.Name, g.Sid, g.MemberDns
            .Select(dn => members.TryGetValue(dn, out var m) ? m : new AdMember(dn, DnUtils.FirstValue(dn), null, false, true))
            .OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList());

    private static string? Name(string? projectDn, string rootOu)
        => projectDn is null ? null : DnUtils.RelativeOuPath(projectDn, rootOu).FirstOrDefault();
}
