namespace WinAdmin.Core.Models;

/// <summary>Сводная техническая информация о машине.</summary>
public sealed record SystemInfo
{
    public required string Hostname { get; init; }
    public string? Domain { get; init; }
    public required string OsName { get; init; }
    public required string OsVersion { get; init; }
    public required string OsArchitecture { get; init; }
    public string? Manufacturer { get; init; }
    public string? Model { get; init; }
    public string? SerialNumber { get; init; }
    public string? BiosVersion { get; init; }
    public string? CpuName { get; init; }
    public int CpuPhysicalCores { get; init; }
    public int CpuLogicalCores { get; init; }
    public long TotalMemoryBytes { get; init; }
    public DateTimeOffset? LastBootTime { get; init; }
    public TimeSpan? Uptime { get; init; }
    public IReadOnlyList<NetworkAdapterInfo> NetworkAdapters { get; init; } = [];
}

/// <summary>Сетевой адаптер с адресами.</summary>
public sealed record NetworkAdapterInfo
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? MacAddress { get; init; }
    public IReadOnlyList<string> IpAddresses { get; init; } = [];
    public bool IsUp { get; init; }
    public long SpeedBitsPerSec { get; init; }
}

/// <summary>Мгновенные метрики нагрузки (для дашборда).</summary>
public sealed record SystemMetrics
{
    public double CpuUsagePercent { get; init; }
    public long MemoryTotalBytes { get; init; }
    public long MemoryUsedBytes { get; init; }
    public double MemoryUsagePercent { get; init; }
    public long NetworkBytesSentPerSec { get; init; }
    public long NetworkBytesReceivedPerSec { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
