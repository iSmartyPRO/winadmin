using Moq;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Infrastructure.ActiveDirectory.Folders;

namespace WinAdmin.Tests;

public sealed class LdapRangeTests
{
    [Theory]
    [InlineData("member;range=0-1499", 1500, false)]
    [InlineData("member;range=1500-*", -1, true)]
    public void Range_attribute_names(string name, int next, bool done)
    {
        var r = LdapRange.Next(name)!.Value;
        Assert.Equal(done, r.Done);
        if (!done) Assert.Equal(next, r.Start);
    }

    [Fact]
    public void Plain_member_is_not_ranged() => Assert.Null(LdapRange.Next("member"));
}

public sealed class LdapAdFolderDirectoryTests
{
    [Fact]
    public async Task Directory_off_is_unavailable()
    {
        var dir = Mock.Of<IDirectorySettingsStore>(m => m.GetAsync(It.IsAny<CancellationToken>()) == Task.FromResult(DirectorySettings.Disabled));
        await Assert.ThrowsAsync<DirectoryUnavailableException>(() => new LdapAdFolderDirectory(dir).ListGroupsAsync("OU=A,DC=x", "sg_"));
    }

    [AdFact]
    public async Task Real_directory_lists_groups_and_resolves_members()
    {
        string E(string n) => Environment.GetEnvironmentVariable(n) ?? "";
        var settings = new DirectorySettings(true, E("WINADMIN_TEST_AD_DOMAIN"), E("WINADMIN_TEST_AD_SERVER"), null, false);
        var dir = Mock.Of<IDirectorySettingsStore>(m => m.GetAsync(It.IsAny<CancellationToken>()) == Task.FromResult(settings));
        var folders = new LdapAdFolderDirectory(dir, new System.Net.NetworkCredential(E("WINADMIN_TEST_AD_USER"), E("WINADMIN_TEST_AD_PASSWORD"), E("WINADMIN_TEST_AD_DOMAIN")));
        var groups = await folders.ListGroupsAsync(E("WINADMIN_TEST_AD_ROOT"), "sg_");
        var members = await folders.ResolveMembersAsync(groups.SelectMany(g => g.MemberDns));
        Assert.All(groups, g => Assert.StartsWith("sg_", g.Name, StringComparison.OrdinalIgnoreCase));
        Assert.All(members.Values, m => Assert.False(string.IsNullOrEmpty(m.Name)));
    }
}
