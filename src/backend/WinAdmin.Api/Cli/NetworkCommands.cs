using System.CommandLine;
using System.CommandLine.Invocation;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Network;
using WinAdmin.Infrastructure.Network;

namespace WinAdmin.Api.Cli;

public static class NetworkCommands
{
    public static void Register(Command networkCommand, IServiceProvider provider, TextWriter? output = null, TextWriter? error = null)
    {
        var o = output ?? Console.Out;
        var e = error ?? Console.Error;
        networkCommand.AddCommand(ShowCommand(provider, o));
        networkCommand.AddCommand(SetCommand(provider, o, e));
    }

    // ── show ─────────────────────────────────────────────────────
    private static Command ShowCommand(IServiceProvider provider, TextWriter output)
    {
        var cmd = new Command("show", "Показать сетевые настройки панели");
        cmd.SetHandler(() =>
        {
            var store = provider.GetRequiredService<NetworkSettingsStore>();
            var s = store.ReadOrDefault(out var problem);
            output.WriteLine($"Файл:     {store.FilePath}");
            if (problem is not null)
                output.WriteLine($"Ошибка:   {problem} (используются значения по умолчанию)");
            output.WriteLine($"Режим:    {(s.Mode == NetworkMode.Local ? "local (только этот компьютер)" : "network (сеть)")}");
            output.WriteLine($"Порт:     {s.Port}");
            output.WriteLine($"Подсети:  {(s.Allow.Count == 0 ? "—" : string.Join(", ", s.Allow))}");
            output.WriteLine($"Адрес:    {NetworkEndpoints.PanelUrl(s, null)}");
        });
        return cmd;
    }

    // ── set ──────────────────────────────────────────────────────
    private static Command SetCommand(IServiceProvider provider, TextWriter output, TextWriter error)
    {
        var modeOpt = new Option<string?>("--mode", "local — только этот компьютер; network — сеть (нужен --allow)");
        var portOpt = new Option<int?>("--port", "Порт панели (1–65535)");
        var allowOpt = new Option<string?>("--allow", "Разрешённые адреса/подсети через запятую, например 10.77.77.0/24,192.168.88.5");
        var cmd = new Command("set", "Изменить сетевые настройки (нужны права администратора)") { modeOpt, portOpt, allowOpt };

        cmd.SetHandler((InvocationContext ctx) =>
        {
            var network = provider.GetRequiredService<INetworkSettingsService>();
            var current = network.Current;

            string? modeRaw = ctx.ParseResult.GetValueForOption(modeOpt);
            var mode = current.Mode;
            if (modeRaw is not null)
            {
                switch (modeRaw.Trim().ToLowerInvariant())
                {
                    case "local": mode = NetworkMode.Local; break;
                    case "network": mode = NetworkMode.Network; break;
                    default:
                        error.WriteLine($"Неизвестный режим «{modeRaw}». Допустимо: local, network.");
                        ctx.ExitCode = 1;
                        return;
                }
            }

            int port = ctx.ParseResult.GetValueForOption(portOpt) ?? current.Port;
            string? allowRaw = ctx.ParseResult.GetValueForOption(allowOpt);
            IReadOnlyList<string> allow = allowRaw is null
                ? current.Allow
                : allowRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var check = network.Check(new NetworkSettings(mode, port, allow));
            foreach (var message in check.Errors)
                error.WriteLine(message);
            if (check.PortBusy)
                error.WriteLine($"Порт {check.Normalized.Port} уже занят другой программой.");
            if (!check.Ok)
            {
                ctx.ExitCode = 1;
                return;
            }

            try
            {
                network.Apply(check.Normalized, current);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException or IOException)
            {
                error.WriteLine($"Не удалось применить настройки: {ex.Message}");
                error.WriteLine("Запустите консоль от имени администратора.");
                ctx.ExitCode = 1;
                return;
            }

            output.WriteLine($"Сохранено в {provider.GetRequiredService<NetworkSettingsStore>().FilePath}");
            output.WriteLine($"Панель: {NetworkEndpoints.PanelUrl(check.Normalized, null)}");
            output.WriteLine("Запущенная служба применит настройки автоматически (без перезапуска).");
        });
        return cmd;
    }
}
