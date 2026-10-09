using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WinAdmin.Api.Network;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Infrastructure.Network;

namespace WinAdmin.Tests;

public sealed class NetworkApplyWatchdogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winadmin-wd-" + Guid.NewGuid().ToString("N"));
    private readonly NetworkSettingsStore _store;
    private readonly Mock<IPortProbe> _probe = new();
    private readonly Mock<IAuditService> _audit = new();
    private readonly NetworkApplyWatchdog _watchdog;

    private static readonly NetworkSettings Previous = new(NetworkMode.Local, 8080, []);
    private static readonly NetworkSettings Applied = new(NetworkMode.Local, 9090, []);

    public NetworkApplyWatchdogTests()
    {
        _store = new NetworkSettingsStore(_dir);
        _store.Write(Applied);
        var network = new NetworkSettingsService(_store, Mock.Of<IFirewallRunner>(), _probe.Object);
        var scopes = new ServiceCollection().AddSingleton(_audit.Object).BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["WinAdmin:Network:VerifyDelaySeconds"] = "0" })
            .Build();
        _watchdog = new NetworkApplyWatchdog(network, _probe.Object, scopes, config, NullLogger<NetworkApplyWatchdog>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task Not_listening_restores_previous_settings_and_audits_rollback()
    {
        _probe.Setup(p => p.IsListening(It.IsAny<NetworkSettings>())).Returns(false);

        await _watchdog.Schedule(Previous, Applied, "admin", "127.0.0.1");

        Assert.True(_store.ReadOrDefault(out _).IsEquivalentTo(Previous));
        _audit.Verify(a => a.WriteAsync(It.Is<AuditEntryDto>(e => e.Action == "settings.network.rollback" && !e.Success),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Listening_keeps_applied_settings()
    {
        _probe.Setup(p => p.IsListening(It.IsAny<NetworkSettings>())).Returns(true);

        await _watchdog.Schedule(Previous, Applied, "admin", null);

        Assert.True(_store.ReadOrDefault(out _).IsEquivalentTo(Applied));
        _audit.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Newer_settings_are_not_rolled_back()
    {
        // После применения 9090 оператор успел выставить 9191 (CLI или второй запрос).
        var newer = new NetworkSettings(NetworkMode.Local, 9191, []);
        _store.Write(newer);
        _probe.Setup(p => p.IsListening(It.IsAny<NetworkSettings>())).Returns(false);

        await _watchdog.Schedule(Previous, Applied, "admin", null);

        Assert.True(_store.ReadOrDefault(out _).IsEquivalentTo(newer));
        _audit.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Audit_failure_does_not_fault_the_check()
    {
        _probe.Setup(p => p.IsListening(It.IsAny<NetworkSettings>())).Returns(false);
        _audit.Setup(a => a.WriteAsync(It.IsAny<AuditEntryDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db locked"));

        await _watchdog.Schedule(Previous, Applied, "admin", null); // не должно бросить

        Assert.True(_store.ReadOrDefault(out _).IsEquivalentTo(Previous));
    }
}
