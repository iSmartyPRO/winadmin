using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using WinAdmin.Api.Network;
using WinAdmin.Core.Models;
using WinAdmin.Infrastructure.Network;
using WinAdmin.Infrastructure.Secrets;

namespace WinAdmin.Tests;

/// <summary>Настоящий Kestrel: включение HTTPS, перенаправление, HSTS и HTTP/1.1 — перепривязкой на лету.</summary>
public sealed class KestrelHttpsTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winadmin-kestrel-https-" + Guid.NewGuid().ToString("N"));
    private readonly FakeCertificateStore _certs = new();
    private readonly X509Certificate2 _cert = TestCertificates.Valid("localhost");
    private NetworkSettingsStore _store = null!;
    private HttpsCertificateHolder _holder = null!;
    private WebApplication _app = null!;
    private X509Certificate2? _seen;
    private HttpClient _http = null!;

    public async Task InitializeAsync()
    {
        _certs.Items.Add((CertificateStoreLocation.LocalMachine, _cert));
        _store = new NetworkSettingsStore(_dir);
        _store.Write(new NetworkSettings(NetworkMode.Local, FreePort(), []));
        var loader = new HttpsCertificateLoader(_certs, new AesGcmSecretProtector(RandomNumberGenerator.GetBytes(32)), Path.Combine(_dir, "keys"));
        _holder = new HttpsCertificateHolder(loader);

        var builder = WebApplication.CreateBuilder();
        builder.UseWinAdminNetwork(_store, _holder);
        _app = builder.Build();
        _app.UseWinAdminHttps();
        _app.MapGet("/{**path}", (HttpContext ctx) => $"ok {ctx.Request.Protocol}");
        await _app.StartAsync();

        _http = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            SslOptions = { RemoteCertificateValidationCallback = (_, cert, _, _) => { _seen = cert is null ? null : new X509Certificate2(cert); return true; } },
        }) { Timeout = TimeSpan.FromSeconds(3) };
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        _http.Dispose();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private NetworkSettings HttpsSettings(int port, int? redirect)
        => new(NetworkMode.Local, port, [],
            new HttpsSettings(true, new HttpsCertificateSettings(HttpsCertificateSource.Store, _cert.Thumbprint), redirect));

    [Fact]
    public async Task Enabling_https_rebinds_with_certificate_http1_hsts_and_redirect()
    {
        int port = FreePort(), redirect = FreePort();
        _store.Write(HttpsSettings(port, redirect));

        // HTTPS на новом порту, сертификат из хранилища, HTTP/2 не согласуется (Negotiate).
        HttpResponseMessage? response = null;
        Assert.True(await EventuallyAsync(async () =>
        {
            response = await TryAsync(new HttpRequestMessage(HttpMethod.Get, $"https://127.0.0.1:{port}/x")
            {
                Version = HttpVersion.Version20,
                VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            });
            return response?.IsSuccessStatusCode == true;
        }));
        Assert.Equal(HttpVersion.Version11, response!.Version);
        Assert.Equal("ok HTTP/1.1", await response.Content.ReadAsStringAsync());
        Assert.Equal(_cert.Thumbprint, _seen?.Thumbprint);
        Assert.Equal("max-age=31536000", response.Headers.GetValues("Strict-Transport-Security").Single());

        // HTTP-порт перенаправления: 308 на тот же путь по HTTPS, без HSTS.
        var redirected = await TryAsync(new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{redirect}/api/v1/x?y=1"));
        Assert.NotNull(redirected);
        Assert.Equal((HttpStatusCode)308, redirected!.StatusCode);
        Assert.Equal($"https://127.0.0.1:{port}/api/v1/x?y=1", redirected.Headers.Location!.ToString());
        Assert.False(redirected.Headers.Contains("Strict-Transport-Security"));

        // TLS-рукопожатие для watchdog.
        Assert.Equal(_cert.Thumbprint, await new TlsProbe().HandshakeAsync(port, CancellationToken.None));
    }

    [Fact]
    public async Task Disabling_https_returns_to_plain_http()
    {
        int port = FreePort();
        _store.Write(HttpsSettings(port, null));
        Assert.True(await EventuallyAsync(async () => (await TryAsync(new(HttpMethod.Get, $"https://127.0.0.1:{port}/")))?.IsSuccessStatusCode == true));

        _store.Write(new NetworkSettings(NetworkMode.Local, port, []));
        Assert.True(await EventuallyAsync(async () => (await TryAsync(new(HttpMethod.Get, $"http://127.0.0.1:{port}/")))?.IsSuccessStatusCode == true));
        Assert.Null(await new TlsProbe().HandshakeAsync(port, CancellationToken.None));
    }

    private async Task<HttpResponseMessage?> TryAsync(HttpRequestMessage request)
    {
        try { return await _http.SendAsync(request); }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static async Task<bool> EventuallyAsync(Func<Task<bool>> condition)
    {
        for (int i = 0; i < 50; i++)
        {
            if (await condition()) return true;
            await Task.Delay(100);
        }
        return false;
    }
}

public sealed class HttpsRedirectTests
{
    private static readonly NetworkSettings Https = new(NetworkMode.Network, 8443, ["10.0.0.0/8"],
        new HttpsSettings(true, new HttpsCertificateSettings(HttpsCertificateSource.SelfSigned), 8080));

    [Fact]
    public void Plain_request_on_redirect_port_goes_to_https_with_same_path()
        => Assert.Equal("https://srv01.corp.local:8443/app/settings?tab=1",
            HttpsRedirect.Location(Https, isHttps: false, localPort: 8080, new HostString("srv01.corp.local:8080"),
                new PathString("/app"), new PathString("/settings"), new QueryString("?tab=1")));

    [Fact]
    public void Standard_https_port_is_omitted()
        => Assert.Equal("https://srv01/",
            HttpsRedirect.Location(Https with { Port = 443 }, false, 8080, new HostString("srv01"), PathString.Empty, new PathString("/"), QueryString.Empty));

    [Fact]
    public void Ipv6_host_keeps_brackets()
        => Assert.Equal("https://[::1]:8443/",
            HttpsRedirect.Location(Https, false, 8080, new HostString("[::1]:8080"), PathString.Empty, new PathString("/"), QueryString.Empty));

    [Theory]
    [InlineData(true, 8080)]   // уже HTTPS
    [InlineData(false, 8443)]  // не порт перенаправления
    [InlineData(false, 0)]     // TestServer
    public void Other_requests_are_not_redirected(bool isHttps, int localPort)
        => Assert.Null(HttpsRedirect.Location(Https, isHttps, localPort, new HostString("srv01"), PathString.Empty, new PathString("/"), QueryString.Empty));

    [Fact]
    public void Disabled_https_never_redirects()
        => Assert.Null(HttpsRedirect.Location(Https with { Https = Https.Https! with { Enabled = false } }, false, 8080,
            new HostString("srv01"), PathString.Empty, new PathString("/"), QueryString.Empty));
}
