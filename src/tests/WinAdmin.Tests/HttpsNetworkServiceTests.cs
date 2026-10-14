using System.Security.Cryptography;
using Moq;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Network;
using WinAdmin.Infrastructure.Network;
using WinAdmin.Infrastructure.Secrets;

namespace WinAdmin.Tests;

public sealed class HttpsNetworkServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winadmin-httpssvc-" + Guid.NewGuid().ToString("N"));
    private readonly NetworkSettingsStore _store;
    private readonly Mock<IFirewallRunner> _firewall = new();
    private readonly Mock<IPortProbe> _probe = new();
    private readonly FakeCertificateStore _certs = new();
    private readonly ISecretProtector _protector = new AesGcmSecretProtector(RandomNumberGenerator.GetBytes(32));
    private readonly NetworkSettingsService _service;

    public HttpsNetworkServiceTests()
    {
        _store = new NetworkSettingsStore(_dir);
        _store.Write(new NetworkSettings(NetworkMode.Network, 8080, ["10.0.0.0/8"]));
        var loader = new HttpsCertificateLoader(_certs, _protector, Path.Combine(_dir, "keys"));
        _service = new NetworkSettingsService(_store, _firewall.Object, _probe.Object, loader);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static NetworkSettings Https(string thumbprint, int port = 8443, int? redirect = null)
        => new(NetworkMode.Network, port, ["10.0.0.0/8"],
            new HttpsSettings(true, new HttpsCertificateSettings(HttpsCertificateSource.Store, thumbprint), redirect));

    [Fact]
    public void Missing_certificate_is_a_validation_error()
    {
        var result = _service.Check(Https(new string('A', 40)));
        Assert.False(result.Ok);
        Assert.Contains(result.Errors, e => e.Contains("не найден"));
    }

    [Fact]
    public void Expired_certificate_is_a_validation_error()
    {
        var cert = TestCertificates.Create(["a.local"], DateTimeOffset.UtcNow.AddYears(-2), DateTimeOffset.UtcNow.AddDays(-1));
        _certs.Items.Add((CertificateStoreLocation.LocalMachine, cert));
        var result = _service.Check(Https(cert.Thumbprint));
        Assert.Contains(result.Errors, e => e.Contains("истёк"));
    }

    [Fact]
    public void Valid_certificate_passes()
    {
        var cert = TestCertificates.Valid();
        _certs.Items.Add((CertificateStoreLocation.LocalMachine, cert));
        var result = _service.Check(Https(cert.Thumbprint));
        Assert.True(result.Ok, string.Join("; ", result.Errors));
    }

    [Fact]
    public void Busy_redirect_port_is_reported()
    {
        var cert = TestCertificates.Valid();
        _certs.Items.Add((CertificateStoreLocation.LocalMachine, cert));
        _probe.Setup(p => p.IsInUse(80)).Returns(true);

        var result = _service.Check(Https(cert.Thumbprint, redirect: 80));
        Assert.Equal(80, result.BusyPort);
        Assert.False(result.Ok);
    }

    [Fact]
    public void Ports_we_listen_on_now_are_not_probed()
    {
        // HTTPS на 8443, а старый HTTP-порт 8080 становится портом перенаправления: его занимаем мы сами.
        var cert = TestCertificates.Valid();
        _certs.Items.Add((CertificateStoreLocation.LocalMachine, cert));
        _probe.Setup(p => p.IsInUse(It.IsAny<int>())).Returns(true);

        var result = _service.Check(Https(cert.Thumbprint, port: 8443, redirect: 8080));
        Assert.Equal(8443, result.BusyPort);
        _probe.Verify(p => p.IsInUse(8080), Times.Never);
    }

    [Fact]
    public void Firewall_rule_opens_redirect_port_too()
    {
        var cmds = FirewallCommands.Build(Https(new string('A', 40), 8443, 8080), []);
        var add = Assert.Single(cmds, c => c.Args[2] == "add");
        Assert.Contains("localport=8443,8080", add.Args);

        var disabled = Https(new string('A', 40), 8443, 8080);
        disabled = disabled with { Https = disabled.Https! with { Enabled = false } };
        Assert.Contains("localport=8443", Assert.Single(FirewallCommands.Build(disabled, []), c => c.Args[2] == "add").Args);
    }

    // ── Слияние запроса с текущими настройками ───────────────────

    private string Protect(string s) => _protector.Protect(s, HttpsCertificateLoader.PfxPasswordPurpose);

    [Fact]
    public void Request_without_https_keeps_previous()
    {
        var previous = new HttpsSettings(true, new HttpsCertificateSettings(HttpsCertificateSource.SelfSigned), 80);
        Assert.Same(previous, HttpsSettingsUpdate.Merge(null, previous, Protect));
    }

    [Fact]
    public void Pfx_password_is_encrypted_and_kept_when_not_sent()
    {
        var first = HttpsSettingsUpdate.Merge(
            new UpdateHttpsRequest(true, HttpsCertificateSource.Pfx, null, null, @"C:\certs\a.pfx", "secret-1", 0), null, Protect);
        Assert.Null(first.HttpRedirectPort);
        string stored = first.Certificate!.Password!;
        Assert.True(ProtectedValue.IsProtected(stored));
        Assert.DoesNotContain("secret-1", stored);
        Assert.Equal("secret-1", _protector.Unprotect(stored, HttpsCertificateLoader.PfxPasswordPurpose));

        var second = HttpsSettingsUpdate.Merge(
            new UpdateHttpsRequest(true, HttpsCertificateSource.Pfx, null, null, @"C:\certs\a.pfx", null, 80), first, Protect);
        Assert.Equal(stored, second.Certificate!.Password);
        Assert.Equal(80, second.HttpRedirectPort);
    }

    [Fact]
    public void Switching_source_drops_pfx_password()
    {
        var pfx = new HttpsSettings(true, new HttpsCertificateSettings(HttpsCertificateSource.Pfx, Path: @"C:\a.pfx", Password: Protect("x")));
        var store = HttpsSettingsUpdate.Merge(
            new UpdateHttpsRequest(true, HttpsCertificateSource.Store, "ab", CertificateStoreLocation.CurrentUser, null, null, null), pfx, Protect);
        Assert.Equal(new HttpsCertificateSettings(HttpsCertificateSource.Store, "ab", CertificateStoreLocation.CurrentUser), store.Certificate);
    }

    [Fact]
    public void Disabling_without_source_keeps_certificate()
    {
        var previous = new HttpsSettings(true, new HttpsCertificateSettings(HttpsCertificateSource.SelfSigned), 80);
        var merged = HttpsSettingsUpdate.Merge(new UpdateHttpsRequest(false, null, null, null, null, null, 80), previous, Protect);
        Assert.False(merged.Enabled);
        Assert.Equal(previous.Certificate, merged.Certificate);
    }
}
