using System.Net;
using Moq;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Infrastructure.ActiveDirectory;

namespace WinAdmin.Tests;

public sealed class LdapDirectoryServiceTests
{
    private static IDirectorySettingsStore Settings(DirectorySettings s)
        => Mock.Of<IDirectorySettingsStore>(m => m.GetAsync(It.IsAny<CancellationToken>()) == Task.FromResult(s));

    [Fact]
    public void Sid_bytes_are_decoded()
        => Assert.Equal("S-1-5-18", LdapMapping.SidFromBytes([1, 1, 0, 0, 0, 0, 0, 5, 18, 0, 0, 0]));

    [Theory]
    [InlineData("512", true)]
    [InlineData("514", false)]
    [InlineData(null, true)]
    public void Account_disabled_flag(string? uac, bool enabled)
        => Assert.Equal(enabled, LdapMapping.IsEnabled(uac));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_password_is_rejected_without_bind(string password)
    {
        // Сервер заведомо недоступен: если бы bind выполнялся — было бы исключение, а не false.
        var service = new LdapDirectoryService(Settings(new DirectorySettings(true, "invalid.example", "127.0.0.1", null, false)));
        Assert.False(await service.ValidateCredentialsAsync("user", password));
    }

    [Fact]
    public async Task Disabled_directory_throws_unavailable()
    {
        var service = new LdapDirectoryService(Settings(DirectorySettings.Disabled));
        await Assert.ThrowsAsync<DirectoryUnavailableException>(() => service.FindUserAsync("user"));
    }

    [Fact]
    public async Task Unreachable_server_throws_unavailable()
    {
        var service = new LdapDirectoryService(Settings(new DirectorySettings(true, "invalid.example", "127.0.0.1", "DC=x", false)));
        await Assert.ThrowsAsync<DirectoryUnavailableException>(() => service.FindUserAsync("user"));
    }

    private static LdapDirectoryService Real()
    {
        string E(string n) => Environment.GetEnvironmentVariable(n) ?? "";
        var settings = new DirectorySettings(true, E("WINADMIN_TEST_AD_DOMAIN"), E("WINADMIN_TEST_AD_SERVER"), null, false);
        return new LdapDirectoryService(Settings(settings),
            new NetworkCredential(E("WINADMIN_TEST_AD_USER"), E("WINADMIN_TEST_AD_PASSWORD"), E("WINADMIN_TEST_AD_DOMAIN")));
    }

    [AdFact]
    public async Task Real_directory_finds_user_and_groups()
    {
        var ad = Real();
        var user = await ad.FindUserAsync(Environment.GetEnvironmentVariable("WINADMIN_TEST_AD_USER")!);
        Assert.NotNull(user);
        Assert.StartsWith("S-1-5-21-", user!.Sid);
        Assert.Equal(user.Sid, (await ad.FindBySidAsync(user.Sid))!.Sid);
        Assert.Contains(await ad.GetTokenGroupsAsync(user.Sid), g => g.EndsWith("-513")); // Domain Users
        Assert.NotEmpty(await ad.SearchAsync(user.SamAccountName, DirectoryObjectKind.User, 5));
        Assert.All(await ad.TestConnectionAsync(), s => Assert.True(s.Ok, s.Name + ": " + s.Message));
    }

    [AdFact]
    public async Task Real_directory_validates_password()
    {
        var ad = Real();
        string user = Environment.GetEnvironmentVariable("WINADMIN_TEST_AD_USER")!;
        Assert.True(await ad.ValidateCredentialsAsync(user, Environment.GetEnvironmentVariable("WINADMIN_TEST_AD_PASSWORD")!));
        Assert.False(await ad.ValidateCredentialsAsync(user, "wrong-" + Guid.NewGuid()));
    }
}
