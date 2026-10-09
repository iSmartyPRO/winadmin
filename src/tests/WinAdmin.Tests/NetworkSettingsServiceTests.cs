using Moq;
using WinAdmin.Core.Models;
using WinAdmin.Infrastructure.Network;

namespace WinAdmin.Tests;

public sealed class NetworkSettingsServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winadmin-netsvc-" + Guid.NewGuid().ToString("N"));
    private readonly NetworkSettingsStore _store;
    private readonly Mock<IFirewallRunner> _firewall = new();
    private readonly Mock<IPortProbe> _probe = new();
    private readonly NetworkSettingsService _service;

    public NetworkSettingsServiceTests()
    {
        _store = new NetworkSettingsStore(_dir);
        _store.Write(new NetworkSettings(NetworkMode.Local, 8080, []));
        _service = new NetworkSettingsService(_store, _firewall.Object, _probe.Object);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Same_port_is_not_reported_busy_even_though_we_listen_on_it()
    {
        _probe.Setup(p => p.IsInUse(8080)).Returns(true);
        var result = _service.Check(new NetworkSettings(NetworkMode.Network, 8080, ["10.0.0.0/8"]));
        Assert.True(result.Ok);
        _probe.Verify(p => p.IsInUse(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public void Busy_new_port_is_reported()
    {
        _probe.Setup(p => p.IsInUse(9090)).Returns(true);
        var result = _service.Check(new NetworkSettings(NetworkMode.Local, 9090, []));
        Assert.True(result.PortBusy);
        Assert.False(result.Ok);
    }

    [Fact]
    public void Invalid_settings_skip_port_probe()
    {
        var result = _service.Check(new NetworkSettings(NetworkMode.Network, 9090, []));
        Assert.NotEmpty(result.Errors);
        _probe.Verify(p => p.IsInUse(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public void Apply_runs_firewall_then_writes_file_and_cleans_previous_port_rule()
    {
        IReadOnlyList<FirewallCommand>? ran = null;
        _firewall.Setup(f => f.Run(It.IsAny<IReadOnlyList<FirewallCommand>>())).Callback<IReadOnlyList<FirewallCommand>>(c => ran = c);
        var previous = _service.Current;
        var next = new NetworkSettings(NetworkMode.Network, 9090, ["10.0.0.0/8"]);

        _service.Apply(next, previous);

        Assert.True(_service.Current.IsEquivalentTo(next));
        Assert.NotNull(ran);
        Assert.Contains(ran!, c => c.Args.Contains("name=WinAdmin HTTP 8080"));
        Assert.Contains(ran!, c => c.Args[2] == "add");
    }

    [Fact]
    public void Firewall_failure_leaves_file_untouched()
    {
        _firewall.Setup(f => f.Run(It.IsAny<IReadOnlyList<FirewallCommand>>()))
            .Throws(new InvalidOperationException("Требуется повышение прав"));
        var previous = _service.Current;

        Assert.Throws<InvalidOperationException>(() =>
            _service.Apply(new NetworkSettings(NetworkMode.Network, 9090, ["10.0.0.0/8"]), previous));

        Assert.True(_service.Current.IsEquivalentTo(previous));
    }
}
