using System.Management;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;

namespace WinAdmin.Infrastructure.Printers;

/// <summary>Перечисляет принтеры и управляет их очередью через WMI (Win32_Printer).</summary>
[SupportedOSPlatform("windows")]
public sealed class PrinterService : IPrinterService
{
    private readonly ILogger<PrinterService> _logger;

    public PrinterService(ILogger<PrinterService> logger) => _logger = logger;

    public IReadOnlyList<PrinterInfo> GetPrinters()
    {
        var result = new List<PrinterInfo>();
        try
        {
            var jobCounts = GetQueuedJobCounts();
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, PortName, DriverName, Location, Default, Shared, WorkOffline, PrinterStatus FROM Win32_Printer");

            foreach (ManagementObject p in searcher.Get().Cast<ManagementObject>())
            {
                string name = p["Name"]?.ToString() ?? "";
                result.Add(new PrinterInfo
                {
                    Name = name,
                    PortName = p["PortName"]?.ToString(),
                    DriverName = p["DriverName"]?.ToString(),
                    Location = p["Location"]?.ToString(),
                    IsDefault = p["Default"] as bool? ?? false,
                    IsShared = p["Shared"] as bool? ?? false,
                    WorkOffline = p["WorkOffline"] as bool? ?? false,
                    Status = MapStatus(p["PrinterStatus"]),
                    QueuedJobs = jobCounts.GetValueOrDefault(name, 0),
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось перечислить принтеры");
        }
        return result.OrderByDescending(p => p.IsDefault).ThenBy(p => p.Name).ToList();
    }

    public OperationResult Control(string printerName, PrinterAction action)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT * FROM Win32_Printer WHERE Name='{printerName.Replace("'", "''")}'");
            var printer = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
            if (printer is null)
                return OperationResult.Fail($"Принтер '{printerName}' не найден");

            using (printer)
            {
                string method = action switch
                {
                    PrinterAction.Pause => "Pause",
                    PrinterAction.Resume => "Resume",
                    PrinterAction.Purge => "CancelAllJobs",
                    _ => throw new ArgumentOutOfRangeException(nameof(action)),
                };
                var ret = (uint)printer.InvokeMethod(method, null);
                return ret == 0
                    ? OperationResult.Ok($"Принтер '{printerName}': действие '{method}' выполнено")
                    : OperationResult.Fail($"Принтер '{printerName}': WMI вернул код {ret}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ошибка управления принтером {Printer}", printerName);
            return OperationResult.Fail($"Не удалось выполнить действие: {ex.Message}");
        }
    }

    private Dictionary<string, int> GetQueuedJobCounts()
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_PrintJob");
            foreach (ManagementObject job in searcher.Get().Cast<ManagementObject>())
            {
                // Name имеет формат "PrinterName, JobId"
                string raw = job["Name"]?.ToString() ?? "";
                int comma = raw.LastIndexOf(',');
                string printer = comma > 0 ? raw[..comma] : raw;
                counts[printer] = counts.GetValueOrDefault(printer, 0) + 1;
            }
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Не удалось подсчитать задания печати");
        }
        return counts;
    }

    private static string MapStatus(object? status)
        => status is { } s && uint.TryParse(s.ToString(), out var v)
            ? v switch
            {
                1 => "Other",
                2 => "Unknown",
                3 => "Idle",
                4 => "Printing",
                5 => "Warmup",
                6 => "StoppedPrinting",
                7 => "Offline",
                _ => "Unknown",
            }
            : "Unknown";
}
