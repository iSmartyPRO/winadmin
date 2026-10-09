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
    public void Refuses_shared_or_unmarked_folders()
    {
        Directory.CreateDirectory(_dir);
        Assert.False(AclPlan.IsSafeTarget(@"C:\", ["WinAdmin.exe"]));
        Assert.False(AclPlan.IsSafeTarget(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), ["WinAdmin.db"]));
        Assert.False(AclPlan.IsSafeTarget(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), ["WinAdmin.exe"]));
        Assert.False(AclPlan.IsSafeTarget(Environment.GetFolderPath(Environment.SpecialFolder.Windows), ["WinAdmin.exe"]));
        Assert.False(AclPlan.IsSafeTarget(_dir, ["WinAdmin.exe"])); // нет маркера — не наша папка

        File.WriteAllText(Path.Combine(_dir, "network.json"), "{}");
        Assert.True(AclPlan.IsSafeTarget(_dir, ["WinAdmin.db", "network.json"]));
    }

    [Fact]
    public void HardenDirectory_skips_junctions_and_survives_broken_ones()
    {
        string outside = _dir + "-outside";
        Directory.CreateDirectory(outside);
        string outsideFile = Path.Combine(outside, "foreign.txt");
        File.WriteAllText(outsideFile, "x");
        var ofs = new FileInfo(outsideFile).GetAccessControl();
        ofs.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.Read, AccessControlType.Allow));
        new FileInfo(outsideFile).SetAccessControl(ofs);
        try
        {
            Directory.CreateDirectory(_dir);
            Junction(Path.Combine(_dir, "link"), outside);
            Junction(Path.Combine(_dir, "broken"), _dir + "-missing");

            var rules = new List<AclRule> { new(AclPlan.Administrators, FileSystemRights.FullControl), new(Me, FileSystemRights.FullControl) };
            InstallationHardening.HardenDirectory(_dir, rules, NullLogger.Instance); // не должно бросить

            var outsideRules = new FileInfo(outsideFile).GetAccessControl()
                .GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>();
            Assert.Contains(outsideRules, r => r.IdentityReference.Value == "S-1-1-0"); // чужое не тронуто
        }
        finally
        {
            foreach (var j in new[] { "link", "broken" })
                if (Directory.Exists(Path.Combine(_dir, j)) || File.Exists(Path.Combine(_dir, j)))
                    Directory.Delete(Path.Combine(_dir, j));
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void ProtectFile_replaces_file_dacl()
    {
        Directory.CreateDirectory(_dir);
        string file = Path.Combine(_dir, "bootstrap-key.txt");
        File.WriteAllText(file, "sp_secret");

        InstallationHardening.ProtectFile(file,
            [new(AclPlan.Administrators, FileSystemRights.FullControl), new(Me, FileSystemRights.FullControl)],
            NullLogger.Instance);

        var acl = new FileInfo(file).GetAccessControl();
        Assert.True(acl.AreAccessRulesProtected);
        var sids = acl.GetAccessRules(true, true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>().Select(r => r.IdentityReference.Value).Distinct();
        Assert.Equal(new[] { AclPlan.Administrators, Me }.Order(), sids.Order());
    }

    private static void Junction(string link, string target)
    {
        var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true,
        })!;
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
    }

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
