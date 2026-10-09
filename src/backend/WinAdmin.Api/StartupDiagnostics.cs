using System.Diagnostics;
using Microsoft.Extensions.Hosting.WindowsServices;

namespace WinAdmin.Api;

/// <summary>
/// Сообщения о проблемах при старте. Пока логгер приложения не построен, Console.WriteLine
/// у службы никто не видит: предупреждения копятся и пишутся в лог после Build(), а
/// фатальные ошибки — в консоль и (для службы) в журнал событий Windows «Application».
/// </summary>
public static class StartupDiagnostics
{
    private const string EventSource = "WinAdmin";
    private static readonly List<string> Pending = [];

    public static void Warn(string message)
    {
        Console.WriteLine(message);
        lock (Pending) Pending.Add(message);
    }

    /// <summary>Пишет накопленные предупреждения в лог (у службы — в том числе в журнал событий).</summary>
    public static void Flush(ILogger logger)
    {
        lock (Pending)
        {
            foreach (var message in Pending)
                logger.LogWarning("{Message}", message);
            Pending.Clear();
        }
    }

    /// <summary>Фатальная ошибка старта: текст без стектрейса, код возврата 1.</summary>
    public static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        if (WindowsServiceHelpers.IsWindowsService())
        {
            try
            {
                if (!EventLog.SourceExists(EventSource))
                    EventLog.CreateEventSource(EventSource, "Application");
                EventLog.WriteEntry(EventSource, message, EventLogEntryType.Error);
            }
            catch (Exception)
            {
                // Журнал событий недоступен — остаётся вывод в консоль.
            }
        }
        return 1;
    }
}
