using System.Management;
using WinAdmin.Core.Abstractions;

namespace WinAdmin.Infrastructure.MachineInfo;

/// <summary>WMI: Win32_OperatingSystem.ProductType (2 — контроллер домена, 3 — сервер), Win32_ComputerSystem.PartOfDomain.</summary>
public sealed class WmiMachineInfo : IMachineInfo
{
    private readonly Lazy<bool> _server = new(() => Query("SELECT ProductType FROM Win32_OperatingSystem", "ProductType") is uint t && t is 2 or 3);
    private readonly Lazy<bool> _domain = new(() => Query("SELECT PartOfDomain FROM Win32_ComputerSystem", "PartOfDomain") is true);

    private readonly Lazy<string?> _domainName = new(() =>
        Query("SELECT PartOfDomain FROM Win32_ComputerSystem", "PartOfDomain") is true
            ? Query("SELECT Domain FROM Win32_ComputerSystem", "Domain") as string
            : null);

    public bool IsWindowsServer => _server.Value;
    public string? DomainName => _domainName.Value;
    public bool IsDomainJoined => _domain.Value;

    private static object? Query(string wql, string property)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(wql);
            foreach (ManagementObject mo in searcher.Get())
                using (mo) return mo[property];
        }
        catch (ManagementException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }
}
