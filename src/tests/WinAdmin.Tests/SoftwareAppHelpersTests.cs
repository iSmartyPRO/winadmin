using WinAdmin.Infrastructure.Software;

namespace WinAdmin.Tests;

public sealed class SoftwareAppHelpersTests
{
    [Theory]
    [InlineData(1, null, true)]
    [InlineData(0, "KB123456", true)]
    [InlineData(0, null, false)]
    public void IsRegistrySystem_Heuristics(int systemComponent, string? parentKeyName, bool expected)
    {
        Assert.Equal(expected, SoftwareAppHelpers.IsRegistrySystem(systemComponent, parentKeyName, displayName: "App"));
    }

    [Fact]
    public void CanUninstallRegistry_RequiresUninstallString()
    {
        Assert.False(SoftwareAppHelpers.CanUninstallRegistry(null, null));
        Assert.True(SoftwareAppHelpers.CanUninstallRegistry("msiexec /x {GUID}", null));
        Assert.True(SoftwareAppHelpers.CanUninstallRegistry(null, "setup.exe /S"));
    }

    [Fact]
    public void BuildQuietUninstallCommand_PrefersQuietString()
    {
        var cmd = SoftwareAppHelpers.BuildQuietUninstallCommand(
            uninstallString: "msiexec.exe /x {AAA}",
            quietUninstallString: "msiexec.exe /x {AAA} /qn");
        Assert.Equal("msiexec.exe /x {AAA} /qn", cmd);
    }

    [Fact]
    public void BuildQuietUninstallCommand_AugmentsMsiexec()
    {
        var cmd = SoftwareAppHelpers.BuildQuietUninstallCommand("MsiExec.exe /X{AAA-BBB}", null);
        Assert.Contains("/qn", cmd, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("{AAA-BBB}", cmd, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ids_RoundTrip()
    {
        var reg = SoftwareAppHelpers.MakeRegistryId("{GUID}");
        Assert.True(SoftwareAppHelpers.TryParseId(reg, out var kind, out var key));
        Assert.Equal(SoftwareIdKind.Registry, kind);
        Assert.Equal("{GUID}", key);

        var store = SoftwareAppHelpers.MakeStoreId("Foo_1.0.0.0_x64__publisher");
        Assert.True(SoftwareAppHelpers.TryParseId(store, out kind, out key));
        Assert.Equal(SoftwareIdKind.Store, kind);

        var upd = SoftwareAppHelpers.MakeUpdateId("KB5025221");
        Assert.True(SoftwareAppHelpers.TryParseId(upd, out kind, out key));
        Assert.Equal(SoftwareIdKind.Update, kind);
        Assert.Equal("KB5025221", key);
    }

    [Theory]
    [InlineData("Microsoft.Windows.ShellExperienceHost_cw5n1h2txyewy", true)]
    [InlineData("Microsoft.WindowsCalculator_8wekyb3d8bbwe", false)]
    public void IsStoreSystemPackage(string familyOrFull, bool expected)
    {
        Assert.Equal(expected, SoftwareAppHelpers.IsStoreSystemPackage(familyOrFull));
    }
}
