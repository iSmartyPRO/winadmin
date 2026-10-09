using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Network;

namespace WinAdmin.Infrastructure.Network;

public sealed class NetworkSettingsService : INetworkSettingsService
{
    private readonly NetworkSettingsStore _store;
    private readonly IFirewallRunner _firewall;
    private readonly IPortProbe _probe;

    public NetworkSettingsService(NetworkSettingsStore store, IFirewallRunner firewall, IPortProbe probe)
    {
        _store = store;
        _firewall = firewall;
        _probe = probe;
    }

    public NetworkSettings Current => _store.ReadOrDefault(out _);

    public NetworkCheckResult Check(NetworkSettings next)
    {
        var (normalized, errors) = NetworkSettingsValidator.Normalize(next);
        bool busy = errors.Count == 0
            && normalized.Port != Current.Port
            && _probe.IsInUse(normalized.Port);
        return new NetworkCheckResult(normalized, errors, busy);
    }

    public void Apply(NetworkSettings next, NetworkSettings previous)
    {
        ApplyFirewall(next, [previous.Port]);
        _store.Write(next);
    }

    public void ApplyFirewall(NetworkSettings settings, IEnumerable<int> legacyPorts)
        => _firewall.Run(FirewallCommands.Build(settings, legacyPorts));
}
