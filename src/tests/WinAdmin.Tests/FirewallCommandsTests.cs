using WinAdmin.Core.Models;
using WinAdmin.Infrastructure.Network;

namespace WinAdmin.Tests;

public sealed class FirewallCommandsTests
{
    [Fact]
    public void Local_mode_only_deletes_rules()
    {
        var cmds = FirewallCommands.Build(new NetworkSettings(NetworkMode.Local, 9090, ["10.0.0.0/8"]), [8080]);

        Assert.All(cmds, c => Assert.Equal("delete", c.Args[2]));
        Assert.All(cmds, c => Assert.True(c.IgnoreFailure));
        var names = cmds.Select(c => c.Args[4]).ToList();
        Assert.Contains("name=WinAdmin (managed)", names);
        Assert.Contains("name=WinAdmin HTTP 8080", names);
        Assert.Contains("name=WinAdmin HTTP 9090", names);
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void Network_mode_recreates_managed_rule_for_allowed_subnets_only()
    {
        var cmds = FirewallCommands.Build(new NetworkSettings(NetworkMode.Network, 9090, ["10.77.77.0/24", "192.168.88.5"]), []);

        var add = Assert.Single(cmds, c => c.Args[2] == "add");
        Assert.False(add.IgnoreFailure);
        Assert.Equal(
            new[] { "advfirewall", "firewall", "add", "rule", "name=WinAdmin (managed)", "dir=in", "action=allow",
                    "protocol=TCP", "localport=9090", "remoteip=10.77.77.0/24,192.168.88.5", "profile=domain,private" },
            add.Args);
        Assert.Same(add, cmds[^1]); // add — последней, после всех delete
    }
}
