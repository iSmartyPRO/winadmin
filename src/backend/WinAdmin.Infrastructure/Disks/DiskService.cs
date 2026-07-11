using System.Management;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;

namespace WinAdmin.Infrastructure.Disks;

/// <summary>
/// Строит дерево «физический диск → разделы → логические тома» через WMI-ассоциации,
/// чтобы отрисовать заполнение дисков как в проводнике Windows.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DiskService : IDiskService
{
    private readonly ILogger<DiskService> _logger;

    public DiskService(ILogger<DiskService> logger) => _logger = logger;

    public IReadOnlyList<PhysicalDisk> GetDisks()
    {
        var result = new List<PhysicalDisk>();
        try
        {
            using var drives = new ManagementObjectSearcher(
                "SELECT DeviceID, Model, InterfaceType, MediaType, Size, Partitions, SerialNumber FROM Win32_DiskDrive");

            foreach (ManagementObject drive in drives.Get().Cast<ManagementObject>())
            {
                result.Add(new PhysicalDisk
                {
                    Model = drive["Model"]?.ToString()?.Trim() ?? "Unknown disk",
                    InterfaceType = drive["InterfaceType"]?.ToString(),
                    MediaType = drive["MediaType"]?.ToString(),
                    SizeBytes = ToLong(drive["Size"]),
                    Partitions = ToUInt(drive["Partitions"]),
                    SerialNumber = drive["SerialNumber"]?.ToString()?.Trim(),
                    Volumes = GetVolumesForDrive(drive),
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось перечислить физические диски");
        }
        return result;
    }

    private IReadOnlyList<DiskVolume> GetVolumesForDrive(ManagementObject drive)
    {
        var volumes = new List<DiskVolume>();
        try
        {
            // GetRelated обходит ассоциации напрямую — без ручного экранирования WQL,
            // которое ломалось на DeviceID вида \\.\PHYSICALDRIVE0.
            foreach (ManagementObject partition in drive.GetRelated("Win32_DiskPartition").Cast<ManagementObject>())
            {
                foreach (ManagementObject ld in partition.GetRelated("Win32_LogicalDisk").Cast<ManagementObject>())
                {
                    volumes.Add(new DiskVolume
                    {
                        Drive = ld["DeviceID"]?.ToString() ?? "?",
                        Label = ld["VolumeName"]?.ToString(),
                        FileSystem = ld["FileSystem"]?.ToString(),
                        DriveType = MapDriveType(ToUInt(ld["DriveType"])),
                        TotalBytes = ToLong(ld["Size"]),
                        FreeBytes = ToLong(ld["FreeSpace"]),
                    });
                    ld.Dispose();
                }
                partition.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось получить тома диска {Model}", drive["Model"]);
        }
        return volumes;
    }

    private static string MapDriveType(uint t) => t switch
    {
        2 => "Removable",
        3 => "Fixed",
        4 => "Network",
        5 => "CDRom",
        6 => "Ram",
        _ => "Unknown",
    };

    private static long ToLong(object? v) => v != null && long.TryParse(v.ToString(), out var l) ? l : 0;
    private static uint ToUInt(object? v) => v != null && uint.TryParse(v.ToString(), out var u) ? u : 0;
}
