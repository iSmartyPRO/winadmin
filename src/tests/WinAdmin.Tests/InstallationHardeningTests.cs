using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Logging.Abstractions;
using WinAdmin.Infrastructure.Hardening;

namespace WinAdmin.Tests;

public sealed class InstallationHardeningTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winadmin-acl-" + Guid.NewGuid().ToString("N"));
    private static readonly string Me = WindowsIdentity.GetCurrent().User!.Value;

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void App_directory_plan_gives_users_read_only()
    {
        var rules = AclPlan.ForAppDirectory();
        Assert.Contains(rules, r => r.Sid == AclPlan.Administrators && r.Rights == FileSystemRights.FullControl);
        Assert.Contains(rules, r => r.Sid == AclPlan.LocalSystem && r.Rights == FileSystemRights.FullControl);
        Assert.Contains(rules, r => r.Sid == AclPlan.Users && r.Rights == FileSystemRights.ReadAndExecute);
        Assert.Equal(3, rules.Count);
    }

    [Fact]
    public void Data_directory_plan_excludes_users()
    {
        Assert.DoesNotContain(AclPlan.ForDataDirectory(), r => r.Sid == AclPlan.Users);
    }

    [Theory]
    [InlineData(@"C:\apps\WinAdmin", @"C:\apps\WinAdmin\", true)]
    [InlineData(@"C:\apps\WinAdmin", @"c:\APPS\winadmin", true)]
    [InlineData(@"C:\apps\WinAdmin", @"C:\ProgramData\WinAdmin", false)]
    public void Compares_directories(string a, string b, bool same)
        => Assert.Equal(same, AclPlan.SameDirectory(a, b));

    [Fact]
    public void HardenDirectory_replaces_dacl_and_resets_children_to_inherited()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "sub"));
        string file = Path.Combine(_dir, "sub", "app.dll");
        File.WriteAllText(file, "x");
        // Явное разрешение «Все: изменение» на файле — должно исчезнуть.
        var fileInfo = new FileInfo(file);
        var fs = fileInfo.GetAccessControl();
        fs.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.Modify, AccessControlType.Allow));
        fileInfo.SetAccessControl(fs);

        var rules = new List<AclRule>
        {
            new(AclPlan.Administrators, FileSystemRights.FullControl),
            new(Me, FileSystemRights.FullControl), // иначе тест не сможет удалить каталог
        };
        InstallationHardening.HardenDirectory(_dir, rules, NullLogger.Instance);

        var dirAcl = new DirectoryInfo(_dir).GetAccessControl();
        Assert.True(dirAcl.AreAccessRulesProtected);
        var dirSids = dirAcl.GetAccessRules(true, true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>().Select(r => r.IdentityReference.Value).Distinct().ToList();
        Assert.Equal(new[] { AclPlan.Administrators, Me }.Order(), dirSids.Order());

        var fileRules = new FileInfo(file).GetAccessControl()
            .GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>().ToList();
        Assert.All(fileRules, r => Assert.True(r.IsInherited));
        Assert.DoesNotContain(fileRules, r => r.IdentityReference.Value == "S-1-1-0");
    }
}
