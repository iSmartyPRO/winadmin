using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using WinAdmin.Api.Cli;
using WinAdmin.Core.Models;
using WinAdmin.Infrastructure.Network;

namespace WinAdmin.Tests;

public sealed class NetworkCommandsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winadmin-cli-" + Guid.NewGuid().ToString("N"));
    private readonly NetworkSettingsStore _store;
    private readonly Mock<IFirewallRunner> _firewall = new();
    private readonly Mock<IPortProbe> _probe = new();
    private readonly StringWriter _out = new();
    private readonly StringWriter _err = new();
    private readonly RootCommand _root;
    private readonly FakeCertificateStore _certs = new();
    private readonly WinAdmin.Core.Abstractions.ISecretProtector _protector =
        new WinAdmin.Infrastructure.Secrets.AesGcmSecretProtector(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    public NetworkCommandsTests()
    {
        _store = new NetworkSettingsStore(_dir);
        _store.Write(new NetworkSettings(NetworkMode.Local, 8080, []));

        var services = new ServiceCollection();
        services.AddWinAdminNetwork(_store);
        services.AddSingleton(_firewall.Object);
        services.AddSingleton(_probe.Object);
        services.AddSingleton<WinAdmin.Core.Abstractions.ISecretProtector>(_protector);
        services.AddSingleton<ICertificateStore>(_certs);
        var provider = services.BuildServiceProvider();

        var network = new Command("network");
        NetworkCommands.Register(network, provider, _out, _err);
        _root = new RootCommand { network };
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task Show_prints_mode_port_and_url()
    {
        int code = await _root.InvokeAsync(["network", "show"]);
        Assert.Equal(0, code);
        Assert.Contains("local", _out.ToString());
        Assert.Contains("http://127.0.0.1:8080", _out.ToString());
    }

    [Fact]
    public async Task Set_merges_options_with_current_settings()
    {
        int code = await _root.InvokeAsync(["network", "set", "--port", "9191"]);
        Assert.Equal(0, code);
        var s = _store.ReadOrDefault(out _);
        Assert.Equal(NetworkMode.Local, s.Mode);
        Assert.Equal(9191, s.Port);
        Assert.Contains("http://127.0.0.1:9191", _out.ToString());
    }

    [Fact]
    public async Task Set_network_mode_applies_firewall()
    {
        int code = await _root.InvokeAsync(["network", "set", "--mode", "network", "--allow", "10.77.77.0/24, 192.168.88.5"]);
        Assert.Equal(0, code);
        Assert.Equal(new[] { "10.77.77.0/24", "192.168.88.5" }, _store.ReadOrDefault(out _).Allow);
        _firewall.Verify(f => f.Run(It.Is<IReadOnlyList<FirewallCommand>>(c =>
            c.Any(x => x.Args.Contains("remoteip=10.77.77.0/24,192.168.88.5")))), Times.Once);
    }

    [Theory]
    [InlineData("--mode", "banana")]
    [InlineData("--port", "70000")]
    public async Task Invalid_input_exits_1_without_changes(string option, string value)
    {
        int code = await _root.InvokeAsync(["network", "set", option, value]);
        Assert.Equal(1, code);
        Assert.Equal(8080, _store.ReadOrDefault(out _).Port);
        Assert.NotEmpty(_err.ToString());
    }

    [Fact]
    public async Task Busy_port_exits_1()
    {
        _probe.Setup(p => p.IsInUse(9292)).Returns(true);
        int code = await _root.InvokeAsync(["network", "set", "--port", "9292"]);
        Assert.Equal(1, code);
        Assert.Contains("9292", _err.ToString());
    }

    [Fact]
    public async Task Missing_admin_rights_give_clear_message_and_no_changes()
    {
        _firewall.Setup(f => f.Run(It.IsAny<IReadOnlyList<FirewallCommand>>()))
            .Throws(new InvalidOperationException("The requested operation requires elevation."));
        int code = await _root.InvokeAsync(["network", "set", "--mode", "network", "--allow", "10.0.0.0/8"]);
        Assert.Equal(1, code);
        Assert.Contains("от имени администратора", _err.ToString());
        Assert.DoesNotContain("   at ", _err.ToString()); // без стектрейса
        Assert.Equal(NetworkMode.Local, _store.ReadOrDefault(out _).Mode);
    }

    // ── network https ────────────────────────────────────────────

    [Fact]
    public async Task Https_show_reports_disabled()
    {
        Assert.Equal(0, await _root.InvokeAsync(["network", "https", "--show"]));
        Assert.Contains("HTTPS:", _out.ToString());
        Assert.Contains("выключен", _out.ToString());
    }

    [Fact]
    public async Task Https_enable_with_store_thumbprint()
    {
        var cert = TestCertificates.Valid("srv01.corp.local");
        _certs.Items.Add((CertificateStoreLocation.LocalMachine, cert));

        int code = await _root.InvokeAsync(["network", "https", "--enable", "--thumbprint", cert.Thumbprint.ToLowerInvariant()]);

        Assert.Equal(0, code);
        var s = _store.ReadOrDefault(out _);
        Assert.True(s.HttpsEnabled);
        Assert.Equal(cert.Thumbprint, s.Https!.Certificate!.Thumbprint);
        Assert.Equal(CertificateStoreLocation.LocalMachine, s.Https.Certificate.StoreLocation);
        Assert.Contains("https://127.0.0.1:8080", _out.ToString());

        _out.GetStringBuilder().Clear();
        Assert.Equal(0, await _root.InvokeAsync(["network", "https", "--show"]));
        Assert.Contains(cert.Thumbprint, _out.ToString());
        Assert.Contains("действует до", _out.ToString());
    }

    [Fact]
    public async Task Https_enable_with_unknown_thumbprint_fails_without_changes()
    {
        int code = await _root.InvokeAsync(["network", "https", "--enable", "--thumbprint", new string('D', 40)]);
        Assert.Equal(1, code);
        Assert.Contains("не найден", _err.ToString());
        Assert.False(_store.ReadOrDefault(out _).HttpsEnabled);
    }

    [Fact]
    public async Task Https_enable_with_pfx_encrypts_password_and_sets_redirect()
    {
        using var cert = TestCertificates.Valid("srv01.corp.local");
        string pfx = Path.Combine(_dir, "panel.pfx");
        File.WriteAllBytes(pfx, cert.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pfx, "Cli-Secret-7"));

        int code = await _root.InvokeAsync(["network", "https", "--enable", "--pfx", pfx, "--pfx-password", "Cli-Secret-7", "--redirect-port", "8081"]);

        Assert.Equal(0, code);
        string json = File.ReadAllText(_store.FilePath);
        Assert.DoesNotContain("Cli-Secret-7", json);
        Assert.Contains("enc:v1:", json);
        Assert.Equal(8081, _store.ReadOrDefault(out _).Https!.HttpRedirectPort);
        Assert.DoesNotContain("Cli-Secret-7", _out.ToString());
    }

    [Fact]
    public async Task Https_enable_self_signed_generates_certificate_with_extra_dns()
    {
        int code = await _root.InvokeAsync(["network", "https", "--enable", "--self-signed", "--dns", "panel.corp.local"]);

        Assert.Equal(0, code);
        Assert.True(File.Exists(Path.Combine(_dir, "keys", "https.pfx")));
        var s = _store.ReadOrDefault(out _);
        Assert.Equal(HttpsCertificateSource.SelfSigned, s.Https!.Certificate!.Source);
        Assert.Contains("panel.corp.local", _out.ToString());
    }

    [Fact]
    public async Task Https_disable_keeps_certificate_choice()
    {
        await _root.InvokeAsync(["network", "https", "--enable", "--self-signed"]);
        int code = await _root.InvokeAsync(["network", "https", "--disable"]);

        Assert.Equal(0, code);
        var s = _store.ReadOrDefault(out _);
        Assert.False(s.HttpsEnabled);
        Assert.Equal(HttpsCertificateSource.SelfSigned, s.Https!.Certificate!.Source);
        Assert.Contains("http://127.0.0.1:8080", _out.ToString());
    }

    [Fact]
    public async Task Https_conflicting_options_exit_1()
    {
        Assert.Equal(1, await _root.InvokeAsync(["network", "https", "--enable", "--disable"]));
        Assert.Equal(1, await _root.InvokeAsync(["network", "https", "--enable", "--self-signed", "--thumbprint", new string('A', 40)]));
        Assert.Equal(1, await _root.InvokeAsync(["network", "https", "--enable", "--thumbprint", new string('A', 40), "--store", "Nowhere"]));
        Assert.False(_store.ReadOrDefault(out _).HttpsEnabled);
    }
}
