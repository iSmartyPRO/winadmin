using Moq;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Infrastructure.ActiveDirectory;

namespace WinAdmin.Tests;

public sealed class AdCredentialsTests
{
    [Theory]
    [InlineData("PCS\\svc", "svc", "PCS")]
    [InlineData("svc@pcs-msk.com", "svc@pcs-msk.com", "")]
    [InlineData("svc", "svc", "pcs-msk.com")]
    public void Service_account_login_forms(string login, string user, string domain)
    {
        var c = AdCredentials.ToNetwork(new AdWriteCredential(AdWriteMode.ServiceAccount, login, "pw"), "pcs-msk.com")!;
        Assert.Equal(user, c.UserName);
        Assert.Equal(domain, c.Domain);
        Assert.Equal("pw", c.Password);
    }

    [Fact]
    public void Process_account_has_no_credential()
        => Assert.Null(AdCredentials.ToNetwork(new AdWriteCredential(AdWriteMode.ProcessAccount, null, null), "pcs"));

    [Theory]
    [InlineData(null, "pw")]
    [InlineData("svc", null)]
    public void Incomplete_service_account_is_rejected(string? login, string? password)
        => Assert.Throws<ArgumentException>(() =>
            AdCredentials.ToNetwork(new AdWriteCredential(AdWriteMode.ServiceAccount, login, password), "pcs"));
}

public sealed class LdapAdReaderTests
{
    private static LdapAdReader Reader(DirectorySettings directory, AdStructureSettings structure)
    {
        var dir = Mock.Of<IDirectorySettingsStore>(m => m.GetAsync(It.IsAny<CancellationToken>()) == Task.FromResult(directory));
        var st = new Mock<IAdStructureStore>();
        st.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(structure);
        st.Setup(s => s.GetWriteCredentialAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdWriteCredential(AdWriteMode.ServiceAccount, "svc", "pw"));
        return new LdapAdReader(dir, st.Object);
    }

    [Fact]
    public async Task Directory_off_means_unavailable()
        => await Assert.ThrowsAsync<DirectoryUnavailableException>(() =>
            Reader(DirectorySettings.Disabled, AdStructureSettings.Default with { RootOu = "OU=A,DC=x" }).ListProjectsAsync());

    [Fact]
    public async Task Missing_root_is_a_configuration_error()
        => await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Reader(new DirectorySettings(true, "x.local", "127.0.0.1", null, false), AdStructureSettings.Default).ListProjectsAsync());

    [Fact]
    public async Task Unreachable_dc_is_reported_in_writer_status_not_thrown()
    {
        var status = await Reader(new DirectorySettings(true, "x.local", "127.0.0.1", "DC=x", false),
            AdStructureSettings.Default with { RootOu = "OU=A,DC=x" }).GetWriterStatusAsync();
        Assert.False(status.Bound);
        Assert.False(string.IsNullOrEmpty(status.Error));
    }

    private static LdapAdReader Real()
    {
        string E(string n) => Environment.GetEnvironmentVariable(n) ?? "";
        var settings = new DirectorySettings(true, E("WINADMIN_TEST_AD_DOMAIN"), E("WINADMIN_TEST_AD_SERVER"), null, false);
        var dir = Mock.Of<IDirectorySettingsStore>(m => m.GetAsync(It.IsAny<CancellationToken>()) == Task.FromResult(settings));
        var st = new Mock<IAdStructureStore>();
        st.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(AdStructureSettings.Default with { RootOu = E("WINADMIN_TEST_AD_ROOT") });
        st.Setup(s => s.GetWriteCredentialAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdWriteCredential(AdWriteMode.ServiceAccount, E("WINADMIN_TEST_AD_USER"), E("WINADMIN_TEST_AD_PASSWORD")));
        return new LdapAdReader(dir, st.Object, new System.Net.NetworkCredential(E("WINADMIN_TEST_AD_USER"), E("WINADMIN_TEST_AD_PASSWORD"), E("WINADMIN_TEST_AD_DOMAIN")));
    }

    [AdFact]
    public async Task Real_directory_projects_rights_and_writer()
    {
        var ad = Real();
        var projects = await ad.ListProjectsAsync();
        Assert.True(await ad.ExistsAsync(Environment.GetEnvironmentVariable("WINADMIN_TEST_AD_ROOT")!));
        Assert.False(await ad.ExistsAsync("OU=nope-" + Guid.NewGuid().ToString("N") + "," + Environment.GetEnvironmentVariable("WINADMIN_TEST_AD_ROOT")));
        var status = await ad.GetWriterStatusAsync();
        Assert.True(status.Bound, status.Error);
        Assert.True(status.Encrypted);
        if (projects.Count > 0)
            Assert.NotNull(await ad.ReadEffectiveAsync(projects[0].Dn));
    }
}
