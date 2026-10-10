using Moq;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.ActiveDirectory.Folders;
using WinAdmin.Core.EnvironmentChecks;
using WinAdmin.Infrastructure.EnvironmentChecks;
using WinAdmin.Tests.Fakes;

namespace WinAdmin.Tests;

public sealed class AdFoldersCheckTests
{
    private const string Root = "OU=Accounts,DC=test,DC=local";
    private const string A = "OU=A," + Root;
    private readonly FakeAdDomain _ad = new();
    private readonly FakeAdReader _reader = new();
    private readonly FakeNtfs _ntfs = new();
    private AdFoldersSettings _settings = new() { DriveMappings = [@"A=\\fs01\Projects"] };
    private readonly FakeAdDomain.Group _full, _read;

    public AdFoldersCheckTests()
    {
        _reader.Projects.Add(new AdProject(A, "A"));
        _reader.ExistingDns.UnionWith([Root, A]);
        _full = _ad.AddGroup("sg_a_docs_full", A);
        _full.Description = @"A:\Docs;Full Access";
        _read = _ad.AddGroup("sg_a_docs_read", A);
        _read.Description = @"A:\Docs;Read Only";
        _reader.Effective[_full.Dn] = new EffectiveRights(new HashSet<string> { "member" }, new HashSet<string>());
        _reader.Effective[A] = new EffectiveRights(new HashSet<string>(), new HashSet<string> { "group" });
        _ntfs.Directories.UnionWith([@"\\fs01\Projects", @"\\fs01\Projects\Docs"]);
        _ntfs.ChangePermissionRoots.Add(@"\\fs01\Projects");
        _ntfs.Rules[@"\\fs01\Projects\Docs"] = new(StringComparer.OrdinalIgnoreCase) { _full.Sid, _read.Sid };
    }

    private async Task<Dictionary<string, CheckResult>> RunAsync(CheckDepth depth = CheckDepth.Quick)
    {
        var structureSettings = AdStructureSettings.Default with { RootOu = Root };
        var st = Mock.Of<IAdStructureStore>(m => m.GetAsync(It.IsAny<CancellationToken>()) == Task.FromResult(structureSettings));
        var modules = new Mock<IModuleRegistry>();
        modules.Setup(m => m.GetSettingsAsync<AdFoldersSettings>(AdFoldersModule.ModuleId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _settings);
        return (await new AdFoldersCheck(_reader, _ad, _ad, _ntfs, st, modules.Object).RunAsync(depth, default)).ToDictionary(r => r.Code);
    }

    [Fact]
    public async Task Healthy_quick_is_ok_and_acl_scan_only_in_full()
    {
        var quick = await RunAsync();
        Assert.All(quick.Values, r => Assert.Equal(CheckStatus.Ok, r.Status));
        Assert.False(quick.ContainsKey("folders.acl"));
        Assert.Equal(CheckStatus.Ok, (await RunAsync(CheckDepth.Full))["folders.acl"].Status);
    }

    [Fact]
    public async Task Unparsed_groups_and_missing_pairs_are_warnings()
    {
        _ad.AddGroup("sg_a_bad", A).Description = "без пути";
        _ad.AddGroup("sg_a_solo_full", A).Description = @"A:\Solo;Full";
        var r = (await RunAsync())["folders.groups"];
        Assert.Equal(CheckStatus.Warning, r.Status);
        Assert.Contains("sg_a_bad", r.Message);
        Assert.Contains(@"A:\Solo", r.Message);
    }

    [Fact]
    public async Task Unmapped_drive_fails()
    {
        _ad.AddGroup("sg_a_z_full", A).Description = @"Z:\Z;Full";
        var r = (await RunAsync())["folders.mappings"];
        Assert.Equal(CheckStatus.Failed, r.Status);
        Assert.Contains("Z:", r.Message);
    }

    [Fact]
    public async Task Bad_mapping_setting_fails_with_fix()
    {
        _settings = new AdFoldersSettings { DriveMappings = ["A"] };
        Assert.Equal(CheckStatus.Failed, (await RunAsync())["folders.mappings"].Status);
    }

    [Fact]
    public async Task Rights_on_groups_and_create()
    {
        _reader.Effective[_full.Dn] = new EffectiveRights(new HashSet<string>(), new HashSet<string>());
        _reader.Effective[A] = new EffectiveRights(new HashSet<string>(), new HashSet<string>());
        var r = await RunAsync();
        Assert.Equal(CheckStatus.Failed, r["ad.rights.groups"].Status);
        Assert.Equal(CheckStatus.Failed, r["ad.rights.create"].Status);
    }

    [Fact]
    public async Task Share_without_explicit_change_permissions_is_a_warning_and_missing_share_fails()
    {
        _ntfs.ChangePermissionRoots.Clear();
        Assert.Equal(CheckStatus.Warning, (await RunAsync())["folders.share"].Status);
        _ntfs.Directories.Remove(@"\\fs01\Projects");
        Assert.Equal(CheckStatus.Failed, (await RunAsync())["folders.share"].Status);
    }

    [Fact]
    public async Task Full_scan_lists_folders_with_wrong_acl()
    {
        _ntfs.Rules[@"\\fs01\Projects\Docs"].Remove(_read.Sid);
        var r = (await RunAsync(CheckDepth.Full))["folders.acl"];
        Assert.Equal(CheckStatus.Failed, r.Status);
        Assert.Contains(@"A:\Docs", r.Message);
    }
}
