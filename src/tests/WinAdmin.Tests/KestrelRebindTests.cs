using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using WinAdmin.Api.Network;
using WinAdmin.Core.Models;
using WinAdmin.Infrastructure.Network;

namespace WinAdmin.Tests;

public sealed class KestrelRebindTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winadmin-kestrel-" + Guid.NewGuid().ToString("N"));
    private NetworkSettingsStore _store = null!;
    private WebApplication _app = null!;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };

    public async Task InitializeAsync()
    {
        _store = new NetworkSettingsStore(_dir);
        _store.Write(new NetworkSettings(NetworkMode.Local, FreePort(), []));

        var builder = WebApplication.CreateBuilder();
        builder.Configuration.Sources.Add(new NetworkConfigurationSource(_store));
        _app = builder.Build();
        _app.MapGet("/", () => "ok");
        await _app.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        _http.Dispose();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task Rebinds_to_new_port_when_file_changes()
    {
        int oldPort = _store.ReadOrDefault(out _).Port;
        Assert.True(await RespondsAsync($"http://127.0.0.1:{oldPort}/"));

        int newPort = FreePort();
        _store.Write(new NetworkSettings(NetworkMode.Local, newPort, []));

        Assert.True(await EventuallyAsync(() => RespondsAsync($"http://127.0.0.1:{newPort}/")));
        Assert.True(await EventuallyAsync(async () => !await RespondsAsync($"http://127.0.0.1:{oldPort}/")));
    }

    [Fact]
    public async Task Rebinds_when_only_mode_changes()
    {
        var current = _store.ReadOrDefault(out _);
        _store.Write(current with { Mode = NetworkMode.Network, Allow = ["10.0.0.0/8"] });

        var probe = new SystemPortProbe();
        Assert.True(await EventuallyAsync(() =>
            Task.FromResult(probe.IsListening(current with { Mode = NetworkMode.Network }))));
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private async Task<bool> RespondsAsync(string url)
    {
        try { return (await _http.GetStringAsync(url)) == "ok"; }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) { return false; }
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
