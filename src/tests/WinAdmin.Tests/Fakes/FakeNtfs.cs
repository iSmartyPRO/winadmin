using WinAdmin.Core.Abstractions;

namespace WinAdmin.Tests.Fakes;

public sealed class FakeNtfs : INtfsAccess
{
    public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, HashSet<string>> Rules { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ChangePermissionRoots { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Calls { get; } = [];
    public Exception? FailGrant { get; set; }

    public Task<bool> DirectoryExistsAsync(string unc, CancellationToken ct = default) => Task.FromResult(Directories.Contains(unc));

    public Task CreateDirectoryAsync(string unc, CancellationToken ct = default)
    {
        Calls.Add("Create:" + unc);
        Directories.Add(unc);
        return Task.CompletedTask;
    }

    public Task<FolderAclState> InspectAsync(string unc, IReadOnlyList<AclNeed> needs, CancellationToken ct = default)
    {
        if (!Directories.Contains(unc)) return Task.FromResult(new FolderAclState(false, ["папка не найдена"], []));
        var have = Rules.GetValueOrDefault(unc) ?? [];
        return Task.FromResult(new FolderAclState(true,
            needs.Where(n => !have.Contains(n.Sid)).Select(n => $"{n.Label}: нет права «{n.Rights}»").ToList(), []));
    }

    public Task<int> GrantAsync(string unc, IReadOnlyList<AclNeed> needs, CancellationToken ct = default)
    {
        Calls.Add("Grant:" + unc);
        if (FailGrant is not null) throw FailGrant;
        if (!Rules.TryGetValue(unc, out var have)) Rules[unc] = have = new(StringComparer.OrdinalIgnoreCase);
        return Task.FromResult(needs.Count(n => have.Add(n.Sid)));
    }

    public Task<bool> HasExplicitChangePermissionsAsync(string unc, IReadOnlyCollection<string> sids, CancellationToken ct = default)
        => Task.FromResult(ChangePermissionRoots.Contains(unc));
}
