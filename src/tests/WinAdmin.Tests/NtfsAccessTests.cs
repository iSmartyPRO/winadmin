using System.Security.AccessControl;
using System.Security.Principal;
using Moq;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Infrastructure.ActiveDirectory.Folders;

namespace WinAdmin.Tests;

public sealed class NtfsAccessTests : IDisposable
{
    private const string Users = "S-1-5-32-545";          // BUILTIN\Users
    private const string Backup = "S-1-5-32-551";         // BUILTIN\Backup Operators
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winadmin-ntfs-" + Guid.NewGuid().ToString("N"));
    private readonly NtfsAccess _ntfs;

    public NtfsAccessTests()
    {
        var st = Mock.Of<IAdStructureStore>(m => m.GetWriteCredentialAsync(It.IsAny<CancellationToken>()) ==
            Task.FromResult(new AdWriteCredential(AdWriteMode.ProcessAccount, null, null)));
        var dir = Mock.Of<IDirectorySettingsStore>(m => m.GetAsync(It.IsAny<CancellationToken>()) ==
            Task.FromResult(new DirectorySettings(true, "test.local", null, null, false)));
        _ntfs = new NtfsAccess(st, dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static AclNeed[] Needs => [new(Backup, "Full", "Modify"), new(Users, "Read", "ReadAndExecute")];

    [Fact]
    public async Task Create_inspect_grant_and_idempotent_regrant()
    {
        Assert.False(await _ntfs.DirectoryExistsAsync(_dir));
        await _ntfs.CreateDirectoryAsync(_dir);
        Assert.True(await _ntfs.DirectoryExistsAsync(_dir));

        var before = await _ntfs.InspectAsync(_dir, [new(Backup, "Full", "Modify")]);
        Assert.Contains(before.Missing, m => m.Contains("Full"));

        Assert.True(await _ntfs.GrantAsync(_dir, Needs) >= 1);
        var after = await _ntfs.InspectAsync(_dir, Needs);
        Assert.True(after.Ok, string.Join("; ", after.Missing));
        Assert.Equal(0, await _ntfs.GrantAsync(_dir, Needs));

        var rule = new DirectoryInfo(_dir).GetAccessControl().GetAccessRules(true, false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>().Single(r => r.IdentityReference.Value == Backup && !r.IsInherited);
        Assert.Equal(InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, rule.InheritanceFlags);
    }

    [Fact]
    public async Task Grant_keeps_other_rules()
    {
        await _ntfs.CreateDirectoryAsync(_dir);
        int before = new DirectoryInfo(_dir).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier)).Count;
        await _ntfs.GrantAsync(_dir, Needs);
        int after = new DirectoryInfo(_dir).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier)).Count;
        Assert.True(after >= before + 1);
    }

    [Fact]
    public async Task Missing_directory_is_reported()
        => Assert.False((await _ntfs.InspectAsync(_dir, Needs)).Exists);

    [Fact]
    public void Read_group_with_write_rights_is_a_warning()
    {
        var security = new DirectorySecurity();
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(Users), FileSystemRights.Modify,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        var (missing, warnings) = NtfsAccess.Inspect(security, [new(Users, "Read", "ReadAndExecute")]);
        Assert.Empty(missing);
        Assert.Contains(warnings, w => w.Contains("Read"));
    }

    [Theory]
    [InlineData("Modify", FileSystemRights.Modify)]
    [InlineData("readandexecute", FileSystemRights.ReadAndExecute)]
    public void Rights_names(string name, FileSystemRights expected) => Assert.Equal(expected, NtfsAccess.ParseRights(name));

    [Fact]
    public void Unknown_rights_name_is_rejected() => Assert.Throws<ArgumentException>(() => NtfsAccess.ParseRights("Всё"));
}
