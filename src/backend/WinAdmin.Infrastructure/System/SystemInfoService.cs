using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;

namespace WinAdmin.Infrastructure.MachineInfo;

/// <summary>
/// Собирает техническую информацию о машине через WMI и .NET API.
/// Регистрируется как singleton, чтобы переиспользовать счётчики производительности.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SystemInfoService : ISystemInfoService, IDisposable
{
    private readonly ILogger<SystemInfoService> _logger;
    private readonly PerformanceCounter _cpuCounter;
    private readonly Dictionary<string, (PerformanceCounter Sent, PerformanceCounter Recv)> _netCounters = new();
    private readonly Lock _netLock = new();

    public SystemInfoService(ILogger<SystemInfoService> logger)
    {
        _logger = logger;
        _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", readOnly: true);
        _cpuCounter.NextValue(); // первый вызов всегда 0 — «прогреваем»
    }

    public SystemInfo GetSystemInfo()
    {
        var cs = QueryFirst("SELECT Manufacturer, Model, Domain, DNSHostName, NumberOfLogicalProcessors, TotalPhysicalMemory FROM Win32_ComputerSystem");
        var os = QueryFirst("SELECT Caption, Version, OSArchitecture, LastBootUpTime FROM Win32_OperatingSystem");
        var bios = QueryFirst("SELECT SMBIOSBIOSVersion, SerialNumber FROM Win32_BIOS");
        var cpu = QueryFirst("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");

        DateTimeOffset? lastBoot = null;
        if (os?["LastBootUpTime"] is string lb && lb.Length >= 14)
        {
            try { lastBoot = ManagementDateTimeConverter.ToDateTime(lb); }
            catch { /* формат может отличаться */ }
        }

        return new SystemInfo
        {
            Hostname = Environment.MachineName,
            Domain = AsString(cs, "Domain"),
            OsName = AsString(os, "Caption") ?? "Windows",
            OsVersion = AsString(os, "Version") ?? Environment.OSVersion.VersionString,
            OsArchitecture = AsString(os, "OSArchitecture") ?? (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit"),
            Manufacturer = AsString(cs, "Manufacturer"),
            Model = AsString(cs, "Model"),
            SerialNumber = AsString(bios, "SerialNumber"),
            BiosVersion = AsString(bios, "SMBIOSBIOSVersion"),
            CpuName = AsString(cpu, "Name")?.Trim(),
            CpuPhysicalCores = AsInt(cpu, "NumberOfCores"),
            CpuLogicalCores = AsInt(cpu, "NumberOfLogicalProcessors") is var lc and > 0 ? lc : Environment.ProcessorCount,
            TotalMemoryBytes = AsLong(cs, "TotalPhysicalMemory"),
            LastBootTime = lastBoot,
            Uptime = lastBoot is { } b ? DateTimeOffset.Now - b : null,
            NetworkAdapters = GetNetworkAdapters(),
        };
    }

    public SystemMetrics GetMetrics()
    {
        var os = QueryFirst("SELECT FreePhysicalMemory, TotalVisibleMemorySize FROM Win32_OperatingSystem");
        // значения в килобайтах
        long totalKb = AsLong(os, "TotalVisibleMemorySize");
        long freeKb = AsLong(os, "FreePhysicalMemory");
        long totalBytes = totalKb * 1024;
        long usedBytes = (totalKb - freeKb) * 1024;

        var (sent, recv) = ReadNetwork();

        return new SystemMetrics
        {
            CpuUsagePercent = Math.Round(_cpuCounter.NextValue(), 1),
            MemoryTotalBytes = totalBytes,
            MemoryUsedBytes = usedBytes,
            MemoryUsagePercent = totalBytes > 0 ? Math.Round((double)usedBytes / totalBytes * 100, 1) : 0,
            NetworkBytesSentPerSec = sent,
            NetworkBytesReceivedPerSec = recv,
        };
    }

    private (long Sent, long Recv) ReadNetwork()
    {
        try
        {
            lock (_netLock)
            {
                var category = new PerformanceCounterCategory("Network Interface");
                long sent = 0, recv = 0;
                foreach (var instance in category.GetInstanceNames())
                {
                    if (!_netCounters.TryGetValue(instance, out var pair))
                    {
                        pair = (
                            new PerformanceCounter("Network Interface", "Bytes Sent/sec", instance, readOnly: true),
                            new PerformanceCounter("Network Interface", "Bytes Received/sec", instance, readOnly: true));
                        _netCounters[instance] = pair;
                    }
                    sent += (long)pair.Sent.NextValue();
                    recv += (long)pair.Recv.NextValue();
                }
                return (sent, recv);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось прочитать сетевые счётчики");
            return (0, 0);
        }
    }

    private static IReadOnlyList<NetworkAdapterInfo> GetNetworkAdapters()
    {
        var list = new List<NetworkAdapterInfo>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;

            var ips = nic.GetIPProperties().UnicastAddresses
                .Select(a => a.Address.ToString())
                .Where(ip => !ip.StartsWith("fe80", StringComparison.OrdinalIgnoreCase))
                .ToList();

            list.Add(new NetworkAdapterInfo
            {
                Name = nic.Name,
                Description = nic.Description,
                MacAddress = string.Join(":", nic.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2"))),
                IpAddresses = ips,
                IsUp = nic.OperationalStatus == OperationalStatus.Up,
                SpeedBitsPerSec = nic.Speed,
            });
        }
        return list;
    }

    // ── WMI helpers ──────────────────────────────────────────────
    private ManagementBaseObject? QueryFirst(string query)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(query);
            foreach (var o in searcher.Get())
                return o;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ошибка WMI-запроса: {Query}", query);
        }
        return null;
    }

    private static string? AsString(ManagementBaseObject? o, string prop)
        => o?[prop]?.ToString() is { Length: > 0 } s ? s : null;

    private static int AsInt(ManagementBaseObject? o, string prop)
        => o?[prop] is { } v && int.TryParse(v.ToString(), out var i) ? i : 0;

    private static long AsLong(ManagementBaseObject? o, string prop)
        => o?[prop] is { } v && long.TryParse(v.ToString(), out var l) ? l : 0;

    public void Dispose()
    {
        _cpuCounter.Dispose();
        foreach (var (_, pair) in _netCounters)
        {
            pair.Sent.Dispose();
            pair.Recv.Dispose();
        }
    }
}
