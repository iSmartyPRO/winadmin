using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Network;
using WinAdmin.Infrastructure.Network;
using WinAdmin.Infrastructure.Secrets;

namespace WinAdmin.Tests;

/// <summary>Сертификаты для тестов — только в памяти (системные хранилища не трогаем).</summary>
public static class TestCertificates
{
    public static X509Certificate2 Create(string[] dns, DateTimeOffset notBefore, DateTimeOffset notAfter, string? eku = "1.3.6.1.5.5.7.3.1")
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest($"CN={dns[0]}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        foreach (var name in dns) san.AddDnsName(name);
        req.CertificateExtensions.Add(san.Build());
        if (eku is not null)
            req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(eku)], false));
        using var cert = req.CreateSelfSigned(notBefore, notAfter);
        // Ключ в памяти (ephemeral) SChannel не принимает — перезагружаем через PFX.
        return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable);
    }

    public static X509Certificate2 Valid(params string[] dns)
        => Create(dns.Length == 0 ? ["srv01.corp.local"] : dns, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

    public static X509Certificate2 WithoutKey(X509Certificate2 cert) => X509CertificateLoader.LoadCertificate(cert.RawData);
}

/// <summary>Хранилище «Личные» в памяти.</summary>
public sealed class FakeCertificateStore : ICertificateStore
{
    public List<(CertificateStoreLocation Location, X509Certificate2 Certificate)> Items { get; } = [];

    public X509Certificate2? Find(CertificateStoreLocation location, string thumbprint)
        => Items.Where(i => i.Location == location && i.Certificate.Thumbprint == thumbprint).Select(i => i.Certificate).FirstOrDefault();

    public IReadOnlyList<(CertificateStoreLocation Location, X509Certificate2 Certificate)> List() => Items;
}

public sealed class HttpsCertificatesTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winadmin-cert-" + Guid.NewGuid().ToString("N"));
    private readonly ISecretProtector _protector = new AesGcmSecretProtector(RandomNumberGenerator.GetBytes(32));
    private readonly FakeCertificateStore _store = new();
    private readonly HttpsCertificateLoader _loader;

    public HttpsCertificatesTests()
    {
        _loader = new HttpsCertificateLoader(_store, _protector, Path.Combine(_dir, "keys"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    // ── Проверка сертификата ─────────────────────────────────────

    [Fact]
    public void Valid_certificate_has_no_problems_and_is_described()
    {
        using var cert = TestCertificates.Create(["srv01.corp.local", "srv01"], Now.AddDays(-1), Now.AddYears(1));
        Assert.Empty(HttpsCertificates.Problems(cert, Now));

        var info = HttpsCertificates.Describe(cert, CertificateStoreLocation.LocalMachine);
        Assert.Equal(cert.Thumbprint, info.Thumbprint);
        Assert.Equal("CN=srv01.corp.local", info.Subject);
        Assert.Equal(["srv01.corp.local", "srv01"], info.DnsNames);
        Assert.True(info.HasPrivateKey);
        Assert.Equal(CertificateStoreLocation.LocalMachine, info.StoreLocation);
    }

    [Fact]
    public void Expired_certificate_is_rejected()
    {
        using var cert = TestCertificates.Create(["a.local"], Now.AddYears(-2), Now.AddDays(-1));
        Assert.Contains(HttpsCertificates.Problems(cert, Now), p => p.Contains("истёк"));
    }

    [Fact]
    public void Not_yet_valid_certificate_is_rejected()
    {
        using var cert = TestCertificates.Create(["a.local"], Now.AddDays(1), Now.AddYears(1));
        Assert.Contains(HttpsCertificates.Problems(cert, Now), p => p.Contains("ещё не действует"));
    }

    [Fact]
    public void Certificate_without_private_key_is_rejected()
    {
        using var full = TestCertificates.Create(["a.local"], Now.AddDays(-1), Now.AddYears(1));
        using var cert = TestCertificates.WithoutKey(full);
        Assert.Contains(HttpsCertificates.Problems(cert, Now), p => p.Contains("закрытого ключа"));
    }

    [Fact]
    public void Certificate_for_client_authentication_only_is_rejected()
    {
        using var cert = TestCertificates.Create(["a.local"], Now.AddDays(-1), Now.AddYears(1), eku: "1.3.6.1.5.5.7.3.2");
        Assert.Contains(HttpsCertificates.Problems(cert, Now), p => p.Contains("проверки подлинности сервера"));
    }

    [Theory]
    [InlineData("srv01.corp.local", true)]
    [InlineData("SRV01.CORP.LOCAL", true)]
    [InlineData("other.corp.local", true)]   // *.corp.local
    [InlineData("a.b.corp.local", false)]    // wildcard — один уровень
    [InlineData("corp.local", false)]
    [InlineData("srv02", false)]
    public void Host_is_matched_against_san_with_wildcards(string host, bool expected)
    {
        using var cert = TestCertificates.Create(["srv01.corp.local", "*.corp.local"], Now.AddDays(-1), Now.AddYears(1));
        Assert.Equal(expected, HttpsCertificates.CoversHost(cert, host));
    }

    // ── Самоподписанный ──────────────────────────────────────────

    [Fact]
    public void Default_self_signed_names_are_fqdn_short_name_and_localhost()
    {
        Assert.Equal(["SRV01.corp.local", "SRV01", "localhost"], HttpsCertificates.SelfSignedDnsNames("SRV01", "corp.local", []));
        Assert.Equal(["SRV01", "localhost"], HttpsCertificates.SelfSignedDnsNames("SRV01", null, []));
        Assert.Equal(["SRV01.corp.local", "SRV01", "localhost", "panel.corp.local"],
            HttpsCertificates.SelfSignedDnsNames("SRV01", "corp.local", [" panel.corp.local ", "srv01"]));
    }

    [Theory]
    [InlineData("bad name")]
    [InlineData("10.0.0.1")]
    [InlineData("http://x")]
    public void Self_signed_rejects_invalid_dns_names(string name)
        => Assert.Throws<ArgumentException>(() => HttpsCertificates.SelfSignedDnsNames("SRV01", null, [name]));

    [Fact]
    public void Self_signed_certificate_is_rsa_2048_for_two_years_with_san_and_server_auth()
    {
        using var cert = HttpsCertificates.CreateSelfSigned(["srv01.corp.local", "srv01", "localhost"], Now);

        Assert.True(cert.HasPrivateKey);
        Assert.Equal(2048, cert.GetRSAPublicKey()!.KeySize);
        Assert.Equal("CN=srv01.corp.local", cert.Subject);
        Assert.Equal(["srv01.corp.local", "srv01", "localhost"], HttpsCertificates.DnsNames(cert));
        Assert.Equal(Now.AddYears(2).UtcDateTime.Date, cert.NotAfter.ToUniversalTime().Date);
        Assert.True(cert.NotBefore.ToUniversalTime() <= Now.UtcDateTime);
        Assert.Empty(HttpsCertificates.Problems(cert, Now));
    }

    // ── Загрузка по настройкам ───────────────────────────────────

    [Fact]
    public void Store_certificate_is_found_by_thumbprint_and_location()
    {
        var cert = TestCertificates.Valid();
        _store.Items.Add((CertificateStoreLocation.CurrentUser, cert));

        var ok = _loader.Load(new HttpsCertificateSettings(HttpsCertificateSource.Store, cert.Thumbprint, CertificateStoreLocation.CurrentUser));
        Assert.Null(ok.Error);
        Assert.Equal(cert.Thumbprint, ok.Certificate!.Thumbprint);

        var missing = _loader.Load(new HttpsCertificateSettings(HttpsCertificateSource.Store, cert.Thumbprint, CertificateStoreLocation.LocalMachine));
        Assert.Null(missing.Certificate);
        Assert.Contains("не найден", missing.Error);
        Assert.Contains("LocalMachine\\My", missing.Error);
    }

    [Fact]
    public void List_returns_only_certificates_with_private_key()
    {
        var withKey = TestCertificates.Valid("a.local");
        _store.Items.Add((CertificateStoreLocation.LocalMachine, withKey));
        _store.Items.Add((CertificateStoreLocation.LocalMachine, TestCertificates.WithoutKey(TestCertificates.Valid("b.local"))));

        var list = _loader.ListStoreCertificates();
        var single = Assert.Single(list);
        Assert.Equal(withKey.Thumbprint, single.Thumbprint);
    }

    [Fact]
    public void Pfx_is_loaded_with_protected_password()
    {
        Directory.CreateDirectory(_dir);
        using var cert = TestCertificates.Valid();
        string pfx = Path.Combine(_dir, "panel.pfx");
        File.WriteAllBytes(pfx, cert.Export(X509ContentType.Pfx, "P@ss-1"));

        var settings = new HttpsCertificateSettings(HttpsCertificateSource.Pfx, Path: pfx,
            Password: _protector.Protect("P@ss-1", HttpsCertificateLoader.PfxPasswordPurpose));
        var ok = _loader.Load(settings);
        Assert.Null(ok.Error);
        Assert.Equal(cert.Thumbprint, ok.Certificate!.Thumbprint);
        Assert.True(ok.Certificate.HasPrivateKey);

        var wrong = _loader.Load(settings with { Password = _protector.Protect("nope", HttpsCertificateLoader.PfxPasswordPurpose) });
        Assert.Null(wrong.Certificate);
        Assert.Contains("пароль", wrong.Error);

        var missing = _loader.Load(settings with { Path = Path.Combine(_dir, "none.pfx") });
        Assert.Contains("не найден", missing.Error);
    }

    [Fact]
    public void Self_signed_is_saved_to_keys_with_encrypted_password_and_loaded_back()
    {
        var info = _loader.CreateSelfSigned(["srv01.corp.local", "localhost"]);

        string pfx = Path.Combine(_dir, "keys", "https.pfx");
        Assert.True(File.Exists(pfx));
        string password = File.ReadAllText(pfx + ".password");
        Assert.True(ProtectedValue.IsProtected(password));

        var loaded = _loader.Load(new HttpsCertificateSettings(HttpsCertificateSource.SelfSigned));
        Assert.Null(loaded.Error);
        Assert.Equal(info.Thumbprint, loaded.Certificate!.Thumbprint);
        Assert.Equal(["srv01.corp.local", "localhost"], info.DnsNames);

        // Повторная генерация заменяет сертификат.
        var second = _loader.CreateSelfSigned(["srv01.corp.local"]);
        Assert.NotEqual(info.Thumbprint, second.Thumbprint);
        Assert.Equal(second.Thumbprint, _loader.Load(new HttpsCertificateSettings(HttpsCertificateSource.SelfSigned)).Certificate!.Thumbprint);
    }

    [Fact]
    public void Missing_self_signed_certificate_is_reported()
    {
        var result = _loader.Load(new HttpsCertificateSettings(HttpsCertificateSource.SelfSigned));
        Assert.Null(result.Certificate);
        Assert.Contains("Самоподписанный сертификат не создан", result.Error);
    }

    [Fact]
    public void Validate_combines_loading_and_problems()
    {
        var expired = TestCertificates.Create(["a.local"], DateTimeOffset.UtcNow.AddYears(-2), DateTimeOffset.UtcNow.AddDays(-1));
        _store.Items.Add((CertificateStoreLocation.LocalMachine, expired));

        var result = _loader.Validate(new HttpsCertificateSettings(HttpsCertificateSource.Store, expired.Thumbprint, CertificateStoreLocation.LocalMachine));
        Assert.NotNull(result.Info);
        Assert.Contains(result.Errors, e => e.Contains("истёк"));
    }
}
