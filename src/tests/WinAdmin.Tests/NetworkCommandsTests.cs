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

    public NetworkCommandsTests()
    {
        _store = new NetworkSettingsStore(_dir);
        _store.Write(new NetworkSettings(NetworkMode.Local, 8080, []));

        var services = new ServiceCollection();
        services.AddWinAdminNetwork(_store);
        services.AddSingleton(_firewall.Object);
        services.AddSingleton(_probe.Object);
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
}
