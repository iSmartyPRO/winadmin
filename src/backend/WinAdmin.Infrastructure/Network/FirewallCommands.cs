using WinAdmin.Core.Models;
using WinAdmin.Core.Network;

namespace WinAdmin.Infrastructure.Network;

/// <summary>Одна команда netsh (argv без «netsh.exe»).</summary>
public sealed record FirewallCommand(IReadOnlyList<string> Args, bool IgnoreFailure);

/// <summary>Построение команд netsh для правила брандмауэра WinAdmin.</summary>
public static class FirewallCommands
{
    public const string ManagedRuleName = "WinAdmin (managed)";

    /// <summary>Имя правил, которые создавали install-service.ps1 и winadmin-ctl до 1.0.6.</summary>
    public static string LegacyRuleName(int port) => $"WinAdmin HTTP {port}";

    public static IReadOnlyList<FirewallCommand> Build(NetworkSettings settings, IEnumerable<int> legacyPorts)
    {
        var commands = new List<FirewallCommand> { Delete(ManagedRuleName) };
        foreach (int port in legacyPorts.Append(settings.Port).Append(NetworkEndpoints.DefaultPort).Distinct())
            commands.Add(Delete(LegacyRuleName(port)));

        if (settings.Mode == NetworkMode.Network)
        {
            commands.Add(new FirewallCommand(
            [
                "advfirewall", "firewall", "add", "rule",
                $"name={ManagedRuleName}", "dir=in", "action=allow", "protocol=TCP",
                $"localport={settings.Port}",
                $"remoteip={string.Join(',', settings.Allow)}",
                "profile=domain,private",
            ], IgnoreFailure: false));
        }
        return commands;
    }

    // Удаление несуществующего правила — код 1 «No rules match», это не ошибка.
    private static FirewallCommand Delete(string name)
        => new(["advfirewall", "firewall", "delete", "rule", $"name={name}"], IgnoreFailure: true);
}
