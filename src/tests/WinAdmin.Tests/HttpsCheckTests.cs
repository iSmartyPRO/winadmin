using System.Security.Cryptography.X509Certificates;
using Moq;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.EnvironmentChecks;
using WinAdmin.Core.Models;
using WinAdmin.Infrastructure.EnvironmentChecks;

namespace WinAdmin.Tests;

public sealed class HttpsCheckTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 0, 0, 0, TimeSpan.Zero);
    private NetworkSettings _settings = new(NetworkMode.Local, 8080, []);
    private CertificateLoadResult _load = new(null, "нет");
    private string _machine = "srv01.corp.local";

    private async Task<CheckResult> RunAsync()
    {
        var network = new Mock<INetworkSettingsService>();
        network.SetupGet(n => n.Current).Returns(() => _settings);
        var loader = new Mock<IHttpsCertificateLoader>();
        loader.Setup(l => l.Load(It.IsAny<HttpsCertificateSettings>())).Returns(() => _load);
        var check = new HttpsCheck(network.Object, loader.Object, new ManualTimeProvider(Now), () => _machine);
        Assert.Equal("platform", check.ModuleId);
        var result = Assert.Single(await check.RunAsync(CheckDepth.Quick, default));
        Assert.Equal("platform.https", result.Code);
        return result;
    }

    private void EnableWith(X509Certificate2 cert)
    {
        _settings = new NetworkSettings(NetworkMode.Network, 8443, ["10.0.0.0/8"],
            new HttpsSettings(true, new HttpsCertificateSettings(HttpsCertificateSource.SelfSigned)));
        _load = new CertificateLoadResult(cert, null);
    }

    [Fact]
    public async Task Local_mode_without_https_is_ok()
        => Assert.Equal(CheckStatus.Ok, (await RunAsync()).Status);

    [Fact]
    public async Task Network_mode_without_https_warns_about_domain_passwords()
    {
        _settings = new NetworkSettings(NetworkMode.Network, 8080, ["10.0.0.0/8"]);
        var r = await RunAsync();
        Assert.Equal(CheckStatus.Warning, r.Status);
        Assert.Contains("пароли доменных учёток по сети недоступны", r.Message);
        Assert.Contains("HTTPS", r.Fix);
    }

    [Fact]
    public async Task Certificate_that_does_not_load_fails()
    {
        EnableWith(null!);
        _load = new CertificateLoadResult(null, "Сертификат с отпечатком AA не найден");
        var r = await RunAsync();
        Assert.Equal(CheckStatus.Failed, r.Status);
        Assert.Contains("не найден", r.Message);
    }

    [Fact]
    public async Task Expired_certificate_fails()
    {
        EnableWith(TestCertificates.Create(["srv01.corp.local"], Now.AddYears(-1), Now.AddDays(-1)));
        var r = await RunAsync();
        Assert.Equal(CheckStatus.Failed, r.Status);
        Assert.Contains("истёк", r.Message);
    }

    [Fact]
    public async Task Certificate_without_key_fails()
    {
        EnableWith(TestCertificates.WithoutKey(TestCertificates.Create(["srv01.corp.local"], Now.AddDays(-1), Now.AddYears(1))));
        Assert.Equal(CheckStatus.Failed, (await RunAsync()).Status);
    }

    [Fact]
    public async Task Certificate_expiring_within_30_days_warns()
    {
        EnableWith(TestCertificates.Create(["srv01.corp.local"], Now.AddYears(-1), Now.AddDays(20)));
        var r = await RunAsync();
        Assert.Equal(CheckStatus.Warning, r.Status);
        Assert.Contains("истекает", r.Message);
    }

    [Fact]
    public async Task Machine_name_missing_from_san_warns()
    {
        EnableWith(TestCertificates.Create(["other.corp.local"], Now.AddDays(-1), Now.AddYears(1)));
        var r = await RunAsync();
        Assert.Equal(CheckStatus.Warning, r.Status);
        Assert.Contains("srv01.corp.local", r.Message);
    }

    [Fact]
    public async Task Healthy_certificate_is_ok()
    {
        EnableWith(TestCertificates.Create(["srv01.corp.local", "srv01"], Now.AddDays(-1), Now.AddYears(1)));
        var r = await RunAsync();
        Assert.Equal(CheckStatus.Ok, r.Status);
        Assert.Contains("действует до", r.Message);
    }
}
