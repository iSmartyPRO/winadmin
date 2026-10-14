using WinAdmin.Core.Models;
using WinAdmin.Core.Network;
using WinAdmin.Infrastructure.Network;

namespace WinAdmin.Tests;

public sealed class HttpsSettingsTests : IDisposable
{
    private const string Thumb = "0123456789ABCDEF0123456789ABCDEF01234567";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winadmin-https-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static NetworkSettings With(HttpsSettings? https, int port = 8443)
        => new(NetworkMode.Network, port, ["10.0.0.0/8"], https);

    private static HttpsSettings Store(string thumb = Thumb, int? redirect = null)
        => new(true, new HttpsCertificateSettings(HttpsCertificateSource.Store, Thumbprint: thumb), redirect);

    // ── Валидация ────────────────────────────────────────────────

    [Fact]
    public void Missing_https_section_is_valid_and_means_disabled()
    {
        var (s, errors) = NetworkSettingsValidator.Normalize(With(null));
        Assert.Empty(errors);
        Assert.False(s.HttpsEnabled);
    }

    [Fact]
    public void Enabled_without_certificate_is_rejected()
    {
        var (_, errors) = NetworkSettingsValidator.Normalize(With(new HttpsSettings(true)));
        Assert.Contains(errors, e => e.Contains("сертификат", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Thumbprint_copied_from_mmc_is_normalized()
    {
        // В оснастке «Сертификаты» отпечаток копируется с пробелами, строчными буквами и невидимым U+200E.
        string copied = "‎01 23 45 67 89 ab cd ef 01 23 45 67 89 ab cd ef 01 23 45 67";
        var (s, errors) = NetworkSettingsValidator.Normalize(With(Store(copied)));
        Assert.Empty(errors);
        Assert.Equal(Thumb, s.Https!.Certificate!.Thumbprint);
        Assert.Equal(CertificateStoreLocation.LocalMachine, s.Https.Certificate.StoreLocation);
    }

    [Theory]
    [InlineData("")]
    [InlineData("XYZ")]
    [InlineData("0123456789ABCDEF")]
    public void Bad_thumbprint_is_rejected(string thumb)
    {
        var (_, errors) = NetworkSettingsValidator.Normalize(With(Store(thumb)));
        Assert.Contains(errors, e => e.Contains("отпечаток", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Pfx_requires_absolute_path()
    {
        var https = new HttpsSettings(true, new HttpsCertificateSettings(HttpsCertificateSource.Pfx, Path: "panel.pfx"));
        var (_, errors) = NetworkSettingsValidator.Normalize(With(https));
        Assert.Contains(errors, e => e.Contains("PFX"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(70000)]
    [InlineData(8443)]
    public void Redirect_port_must_be_valid_and_differ_from_main(int redirect)
    {
        var (_, errors) = NetworkSettingsValidator.Normalize(With(Store(redirect: redirect)));
        Assert.Contains(errors, e => e.Contains("перенаправлени", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Disabled_https_keeps_certificate_without_validation()
    {
        // Как Allow в режиме Local: выключение не должно терять выбранный сертификат.
        var https = new HttpsSettings(false, new HttpsCertificateSettings(HttpsCertificateSource.Store, Thumbprint: "junk"), 80);
        var (s, errors) = NetworkSettingsValidator.Normalize(With(https));
        Assert.Empty(errors);
        Assert.Equal("junk", s.Https!.Certificate!.Thumbprint);
    }

    // ── Сравнение ────────────────────────────────────────────────

    [Fact]
    public void Equivalence_includes_https()
    {
        var plain = With(null);
        Assert.True(plain.IsEquivalentTo(With(HttpsSettings.Disabled)));
        Assert.False(plain.IsEquivalentTo(With(Store())));
        Assert.True(With(Store()).IsEquivalentTo(With(Store())));
        Assert.False(With(Store()).IsEquivalentTo(With(Store(redirect: 80))));
    }

    // ── network.json ─────────────────────────────────────────────

    [Fact]
    public void Store_round_trips_https_in_camel_case()
    {
        var store = new NetworkSettingsStore(_dir);
        var https = new HttpsSettings(true, new HttpsCertificateSettings(HttpsCertificateSource.SelfSigned), 8080);
        store.Write(With(https));

        string json = File.ReadAllText(store.FilePath);
        Assert.Contains("\"https\"", json);
        Assert.Contains("\"selfSigned\"", json);
        Assert.Contains("\"httpRedirectPort\": 8080", json);

        var read = store.ReadOrDefault(out var error);
        Assert.Null(error);
        Assert.True(read.IsEquivalentTo(With(https)));
    }

    [Fact]
    public void Old_file_without_https_reads_as_disabled()
    {
        Directory.CreateDirectory(_dir);
        var store = new NetworkSettingsStore(_dir);
        File.WriteAllText(store.FilePath, """{ "mode": "local", "port": 8080, "allow": [] }""");
        var read = store.ReadOrDefault(out var error);
        Assert.Null(error);
        Assert.False(read.HttpsEnabled);
    }

    // ── Адреса и Kestrel ─────────────────────────────────────────

    [Fact]
    public void Https_changes_scheme_of_listen_and_panel_urls()
    {
        var s = With(Store());
        Assert.Equal("https://0.0.0.0:8443", NetworkEndpoints.ListenUrl(s));
        Assert.Equal("https://server01.corp.local:8443", NetworkEndpoints.PanelUrl(s, "server01.corp.local"));
        Assert.Equal("https://127.0.0.1:8443", NetworkEndpoints.PanelUrl(s with { Mode = NetworkMode.Local }, null));
    }

    [Fact]
    public void Kestrel_configuration_for_http()
    {
        var cfg = NetworkEndpoints.KestrelConfiguration(new NetworkSettings(NetworkMode.Local, 8080, []));
        Assert.Equal(new Dictionary<string, string?> { ["Kestrel:Endpoints:Http:Url"] = "http://127.0.0.1:8080" }, cfg);
    }

    [Fact]
    public void Kestrel_configuration_for_https_is_http1_only_with_optional_redirect_listener()
    {
        var cfg = NetworkEndpoints.KestrelConfiguration(With(Store(redirect: 8080)));
        Assert.Equal(new Dictionary<string, string?>
        {
            ["Kestrel:Endpoints:Https:Url"] = "https://0.0.0.0:8443",
            // Negotiate (Kerberos SSO) не работает по HTTP/2.
            ["Kestrel:Endpoints:Https:Protocols"] = "Http1",
            ["Kestrel:Endpoints:HttpRedirect:Url"] = "http://0.0.0.0:8080",
        }, cfg);

        var noRedirect = NetworkEndpoints.KestrelConfiguration(With(Store()));
        Assert.DoesNotContain("Kestrel:Endpoints:HttpRedirect:Url", noRedirect.Keys);
    }

    [Fact]
    public void Disabled_https_ignores_redirect_port()
    {
        var s = With(Store(redirect: 8080) with { Enabled = false });
        Assert.Equal("http://0.0.0.0:8443", NetworkEndpoints.ListenUrl(s));
        Assert.Null(NetworkEndpoints.RedirectPort(s));
        Assert.Single(NetworkEndpoints.KestrelConfiguration(s));
    }
}
