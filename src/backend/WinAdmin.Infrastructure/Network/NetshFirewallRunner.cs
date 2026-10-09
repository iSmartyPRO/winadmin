using System.Diagnostics;

namespace WinAdmin.Infrastructure.Network;

/// <summary>Выполняет команды брандмауэра.</summary>
public interface IFirewallRunner
{
    /// <summary>Выполняет команды по порядку; бросает InvalidOperationException на первой ошибке без IgnoreFailure.</summary>
    void Run(IReadOnlyList<FirewallCommand> commands);
}

/// <summary>Реализация через netsh advfirewall (не требует PowerShell).</summary>
public sealed class NetshFirewallRunner : IFirewallRunner
{
    public void Run(IReadOnlyList<FirewallCommand> commands)
    {
        foreach (var command in commands)
        {
            var psi = new ProcessStartInfo("netsh.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (string arg in command.Args)
                psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Не удалось запустить netsh.exe.");
            string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0 && !command.IgnoreFailure)
                throw new InvalidOperationException(
                    $"netsh {string.Join(' ', command.Args)} завершился с кодом {process.ExitCode}: {output.Trim()}");
        }
    }
}
