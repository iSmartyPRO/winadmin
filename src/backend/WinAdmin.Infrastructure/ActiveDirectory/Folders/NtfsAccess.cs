using System.Security.AccessControl;
using System.Security.Principal;
using WinAdmin.Core.Abstractions;

namespace WinAdmin.Infrastructure.ActiveDirectory.Folders;

/// <summary>ACL сетевых папок: Allow по SID с наследованием на подпапки и файлы; чужие правила не трогаются.</summary>
public sealed class NtfsAccess(IAdStructureStore structure, IDirectorySettingsStore directory) : INtfsAccess
{
    private const InheritanceFlags Inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
    private const FileSystemRights WriteBits = FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.Delete
                                               | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    public Task<bool> DirectoryExistsAsync(string unc, CancellationToken ct = default) => RunAsync(() => Directory.Exists(unc), ct);

    public Task CreateDirectoryAsync(string unc, CancellationToken ct = default) => RunAsync(() => Directory.CreateDirectory(unc), ct);

    public Task<FolderAclState> InspectAsync(string unc, IReadOnlyList<AclNeed> needs, CancellationToken ct = default)
        => RunAsync(() =>
        {
            var info = new DirectoryInfo(unc);
            if (!info.Exists) return new FolderAclState(false, ["папка не найдена"], []);
            var (missing, warnings) = Inspect(info.GetAccessControl(), needs);
            return new FolderAclState(true, missing, warnings);
        }, ct);

    public Task<int> GrantAsync(string unc, IReadOnlyList<AclNeed> needs, CancellationToken ct = default)
        => RunAsync(() =>
        {
            var info = new DirectoryInfo(unc);
            var security = info.GetAccessControl();
            int added = 0;
            foreach (var need in needs)
            {
                if (Covered(security, need)) continue;
                security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(need.Sid), ParseRights(need.Rights),
                    Inherit, PropagationFlags.None, AccessControlType.Allow));
                added++;
            }
            if (added > 0) info.SetAccessControl(security);
            return added;
        }, ct);

    public Task<bool> HasExplicitChangePermissionsAsync(string unc, IReadOnlyCollection<string> sids, CancellationToken ct = default)
        => RunAsync(() =>
        {
            var set = new HashSet<string>(sids, StringComparer.OrdinalIgnoreCase);
            return new DirectoryInfo(unc).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .Any(r => r.AccessControlType == AccessControlType.Allow && set.Contains(r.IdentityReference.Value)
                          && (r.FileSystemRights & FileSystemRights.ChangePermissions) != 0);
        }, ct);

    public static (IReadOnlyList<string> Missing, IReadOnlyList<string> Warnings) Inspect(DirectorySecurity security, IReadOnlyList<AclNeed> needs)
    {
        var missing = new List<string>();
        var warnings = new List<string>();
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToList();
        foreach (var need in needs)
        {
            if (!Covered(security, need)) missing.Add($"{need.Label}: нет права «{need.Rights}» для группы {need.Sid}");
            var required = ParseRights(need.Rights);
            var allowed = rules.Where(r => r.AccessControlType == AccessControlType.Allow && r.IdentityReference.Value == need.Sid)
                .Aggregate((FileSystemRights)0, (acc, r) => acc | r.FileSystemRights);
            if ((required & WriteBits) == 0 && (allowed & WriteBits) != 0)
                warnings.Add($"{need.Label}: у группы есть права на запись — проверьте, должна ли она только читать");
        }
        return (missing, warnings);
    }

    public static FileSystemRights ParseRights(string name)
        => Enum.TryParse<FileSystemRights>(name, true, out var rights) && Enum.IsDefined(rights)
            ? rights
            : throw new ArgumentException($"Неизвестные права NTFS «{name}» (Modify, ReadAndExecute, FullControl…).");

    private static bool Covered(DirectorySecurity security, AclNeed need)
    {
        var required = ParseRights(need.Rights);
        return security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
            .Any(r => r.AccessControlType == AccessControlType.Allow
                      && r.IdentityReference.Value == need.Sid
                      && (r.FileSystemRights & required) == required
                      && (r.InheritanceFlags & Inherit) == Inherit
                      && (r.PropagationFlags & PropagationFlags.InheritOnly) == 0);
    }

    private async Task<T> RunAsync<T>(Func<T> action, CancellationToken ct)
    {
        var credential = await structure.GetWriteCredentialAsync(ct);
        string? domain = (await directory.GetAsync(ct)).Domain;
        return await Task.Run(() => Impersonation.Run(credential, domain, action), ct);
    }

    private async Task RunAsync(Action action, CancellationToken ct)
        => await RunAsync(() => { action(); return true; }, ct);
}
