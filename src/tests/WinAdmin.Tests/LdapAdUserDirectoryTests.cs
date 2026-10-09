using Moq;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Infrastructure.ActiveDirectory.Users;

namespace WinAdmin.Tests;

public sealed class LdapValuesTests
{
    [Fact]
    public void File_time_and_never()
    {
        Assert.Equal(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero),
            LdapValues.FileTime(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero).ToFileTime().ToString()));
        Assert.Null(LdapValues.FileTime("0"));
        Assert.Null(LdapValues.FileTime("9223372036854775807"));
        Assert.Null(LdapValues.FileTime(null));
    }

    [Fact]
    public void Generalized_time()
        => Assert.Equal(new DateTimeOffset(2026, 10, 9, 12, 30, 5, TimeSpan.Zero), LdapValues.GeneralizedTime("20261009123005.0Z"));

    [Fact]
    public void Domain_sid_from_user_sid()
        => Assert.Equal("S-1-5-21-1-2-3", LdapValues.DomainSid("S-1-5-21-1-2-3-1105"));
}

public sealed class LdapAdUserDirectoryTests
{
    [Fact]
    public async Task Directory_off_is_unavailable()
    {
        var dir = Mock.Of<IDirectorySettingsStore>(m => m.GetAsync(It.IsAny<CancellationToken>()) == Task.FromResult(DirectorySettings.Disabled));
        var users = new LdapAdUserDirectory(dir, Mock.Of<IAdStructureStore>());
        await Assert.ThrowsAsync<DirectoryUnavailableException>(() => users.FindUserAsync("ivan", []));
    }

    private static LdapAdUserDirectory Real()
    {
        string E(string n) => Environment.GetEnvironmentVariable(n) ?? "";
        var settings = new DirectorySettings(true, E("WINADMIN_TEST_AD_DOMAIN"), E("WINADMIN_TEST_AD_SERVER"), null, false);
        var dir = Mock.Of<IDirectorySettingsStore>(m => m.GetAsync(It.IsAny<CancellationToken>()) == Task.FromResult(settings));
        var cred = new AdWriteCredential(AdWriteMode.ServiceAccount, E("WINADMIN_TEST_AD_USER"), E("WINADMIN_TEST_AD_PASSWORD"));
        var st = Mock.Of<IAdStructureStore>(m => m.GetWriteCredentialAsync(It.IsAny<CancellationToken>()) == Task.FromResult(cred));
        return new LdapAdUserDirectory(dir, st, new System.Net.NetworkCredential(E("WINADMIN_TEST_AD_USER"), E("WINADMIN_TEST_AD_PASSWORD"), E("WINADMIN_TEST_AD_DOMAIN")));
    }

    [AdFact]
    public async Task Real_directory_reads_users_groups_and_domain_users()
    {
        var users = Real();
        string root = Environment.GetEnvironmentVariable("WINADMIN_TEST_AD_ROOT")!;
        var list = await users.ListUsersAsync(root, ["mail", "department"]);
        var me = await users.FindUserAsync(Environment.GetEnvironmentVariable("WINADMIN_TEST_AD_USER")!, ["mail"]);
        Assert.NotNull(me);
        Assert.Contains(await users.GetGroupsAsync(me!), g => g.IsPrimary);
        Assert.EndsWith("-513", (await users.GetDomainUsersGroupAsync()).Sid);
        Assert.NotEmpty(await users.GetWriterSidsAsync());
        Assert.NotNull(await users.ReadSecurityDescriptorAsync(me!.Dn));
        _ = list;
    }
}
