namespace WinAdmin.Core.Models;

/// <summary>Физический диск с его логическими томами (разделами).</summary>
public sealed record PhysicalDisk
{
    public required string Model { get; init; }
    public string? InterfaceType { get; init; }
    public string? MediaType { get; init; }
    public long SizeBytes { get; init; }
    public uint Partitions { get; init; }
    public string? SerialNumber { get; init; }
    public IReadOnlyList<DiskVolume> Volumes { get; init; } = [];
}

/// <summary>Логический том / раздел (как в проводнике Windows).</summary>
public sealed record DiskVolume
{
    /// <summary>Буква тома, например "C:".</summary>
    public required string Drive { get; init; }
    public string? Label { get; init; }
    public string? FileSystem { get; init; }

    /// <summary>Тип носителя: Fixed, Removable, Network, CDRom, Ram.</summary>
    public string DriveType { get; init; } = "Unknown";
    public long TotalBytes { get; init; }
    public long FreeBytes { get; init; }
    public long UsedBytes => TotalBytes - FreeBytes;

    public double UsedPercent =>
        TotalBytes > 0 ? Math.Round((double)UsedBytes / TotalBytes * 100, 1) : 0;
}
