using System.Globalization;
using System.Management;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;

namespace WinAdmin.Infrastructure.Software;

/// <summary>
/// Enumerates installed desktop applications, AppX packages, and Windows updates from local Windows sources.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SoftwareCatalogService : ISoftwareCatalogService
{
    private const string RegistrySource = "Registry";
    private const string StoreSource = "Store";
    private const string RegistryUninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string Wow6432UninstallPath = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

    private static readonly Regex KbArticleRegex = new(@"^KB\d+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ILogger<SoftwareCatalogService> _logger;

    public SoftwareCatalogService(ILogger<SoftwareCatalogService> logger) => _logger = logger;

    public IReadOnlyList<InstalledApp> GetApplications()
    {
        var apps = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);

        AddRegistryApplications(apps);
        AddStoreApplications(apps);

        return apps.Values
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(a => a.Source, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<InstalledUpdate> GetUpdates()
    {
        var updates = new Dictionary<string, InstalledUpdate>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT HotFixID, Description, InstalledOn, InstalledBy FROM Win32_QuickFixEngineering");

            foreach (ManagementObject hotfix in searcher.Get().Cast<ManagementObject>())
            {
                using (hotfix)
                {
                    var hotfixId = GetWmiString(hotfix, "HotFixID");
                    if (string.IsNullOrWhiteSpace(hotfixId))
                        continue;

                    var id = SoftwareAppHelpers.MakeUpdateId(hotfixId);
                    if (updates.ContainsKey(id))
                        continue;

                    var description = GetWmiString(hotfix, "Description");
                    var canUninstall = KbArticleRegex.IsMatch(hotfixId);

                    updates[id] = new InstalledUpdate
                    {
                        Id = id,
                        KbArticle = hotfixId,
                        Title = string.IsNullOrWhiteSpace(description) ? hotfixId : description,
                        Description = description,
                        InstalledOn = ParseWmiDate(GetWmiString(hotfix, "InstalledOn")),
                        CanUninstall = canUninstall,
                        CanRollback = canUninstall,
                    };
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось перечислить установленные обновления");
        }

        return updates.Values
            .OrderByDescending(u => u.InstalledOn ?? DateTime.MinValue)
            .ThenBy(u => u.KbArticle, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public InstalledApp? FindApplication(string id)
        => GetApplications().FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));

    public InstalledUpdate? FindUpdate(string id)
        => GetUpdates().FirstOrDefault(u => string.Equals(u.Id, id, StringComparison.OrdinalIgnoreCase));

    private void AddRegistryApplications(IDictionary<string, InstalledApp> apps)
    {
        ReadRegistryHive(apps, RegistryHive.LocalMachine, RegistryView.Registry64, RegistryUninstallPath);
        ReadRegistryHive(apps, RegistryHive.LocalMachine, RegistryView.Registry64, Wow6432UninstallPath);
        ReadRegistryHive(apps, RegistryHive.CurrentUser, RegistryView.Default, RegistryUninstallPath);
    }

    private void ReadRegistryHive(
        IDictionary<string, InstalledApp> apps,
        RegistryHive hive,
        RegistryView view,
        string uninstallPath)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstallKey = baseKey.OpenSubKey(uninstallPath);
            if (uninstallKey == null)
                return;

            foreach (var subKeyName in uninstallKey.GetSubKeyNames())
            {
                using var appKey = uninstallKey.OpenSubKey(subKeyName);
                if (appKey == null)
                    continue;

                var displayName = GetRegistryString(appKey, "DisplayName");
                if (string.IsNullOrWhiteSpace(displayName))
                    continue;

                var id = SoftwareAppHelpers.MakeRegistryId(subKeyName);
                if (apps.ContainsKey(id))
                    continue;

                var uninstallString = GetRegistryString(appKey, "UninstallString");
                var quietUninstallString = GetRegistryString(appKey, "QuietUninstallString");
                var effectiveUninstallString = SoftwareAppHelpers.BuildQuietUninstallCommand(
                    uninstallString,
                    quietUninstallString);
                var systemComponent = GetRegistryInt(appKey, "SystemComponent");
                var systemHint = GetRegistryString(appKey, "ParentKeyName")
                    ?? GetRegistryString(appKey, "ParentDisplayName");
                var isSystem = SoftwareAppHelpers.IsRegistrySystem(systemComponent, systemHint, displayName);

                apps[id] = new InstalledApp
                {
                    Id = id,
                    Name = displayName,
                    Version = GetRegistryString(appKey, "DisplayVersion"),
                    Publisher = GetRegistryString(appKey, "Publisher"),
                    InstallDate = ParseRegistryDate(GetRegistryString(appKey, "InstallDate")),
                    InstallLocation = GetRegistryString(appKey, "InstallLocation"),
                    SizeBytes = ToSizeBytes(appKey.GetValue("EstimatedSize")),
                    Source = RegistrySource,
                    IsSystem = isSystem,
                    CanUninstall = !isSystem && SoftwareAppHelpers.CanUninstallRegistry(uninstallString, quietUninstallString),
                    UninstallString = effectiveUninstallString,
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Не удалось прочитать реестр приложений {Hive}\\{Path} ({View})",
                hive,
                uninstallPath,
                view);
        }
    }

    private void AddStoreApplications(IDictionary<string, InstalledApp> apps)
    {
        var packageManagerType = ResolvePackageManagerType();
        if (packageManagerType == null)
        {
            _logger.LogWarning("AppX PackageManager API недоступен; каталог приложений будет построен без Store-пакетов");
            return;
        }

        try
        {
            var packageManager = Activator.CreateInstance(packageManagerType);
            var findPackages = packageManagerType.GetMethod("FindPackagesForUser", new[] { typeof(string) });
            if (packageManager == null || findPackages == null)
            {
                _logger.LogWarning("AppX PackageManager API найден, но метод FindPackagesForUser недоступен");
                return;
            }

            var packages = findPackages.Invoke(packageManager, new object[] { string.Empty }) as System.Collections.IEnumerable;
            if (packages == null)
                return;

            foreach (var package in packages)
            {
                AddStoreApplication(apps, package);
            }
        }
        catch (Exception ex) when (ex is TargetInvocationException or TypeLoadException or MissingMethodException or InvalidOperationException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Не удалось перечислить Store/AppX приложения");
        }
    }

    private void AddStoreApplication(IDictionary<string, InstalledApp> apps, object package)
    {
        var packageId = GetPropertyValue(package, "Id");
        var fullName = GetStringProperty(packageId, "FullName");
        if (string.IsNullOrWhiteSpace(fullName))
            return;

        var isFramework = GetBoolProperty(package, "IsFramework");
        if (isFramework)
            return;

        var id = SoftwareAppHelpers.MakeStoreId(fullName);
        if (apps.ContainsKey(id))
            return;

        var name = GetStringProperty(packageId, "Name") ?? fullName;
        var signatureKind = GetStringProperty(package, "SignatureKind");
        var isSystem = SoftwareAppHelpers.IsStoreSystemPackage(fullName)
            || string.Equals(signatureKind, "System", StringComparison.OrdinalIgnoreCase);
        var installLocation = GetPropertyValue(package, "InstalledLocation");

        apps[id] = new InstalledApp
        {
            Id = id,
            Name = name,
            Version = FormatPackageVersion(GetPropertyValue(packageId, "Version")),
            Publisher = GetStringProperty(package, "PublisherDisplayName") ?? GetStringProperty(packageId, "Publisher"),
            InstallDate = GetDateTimeProperty(package, "InstalledDate"),
            InstallLocation = GetStringProperty(installLocation, "Path"),
            SizeBytes = null,
            Source = StoreSource,
            IsSystem = isSystem,
            CanUninstall = !isSystem && !isFramework,
            UninstallString = null,
        };
    }

    private static Type? ResolvePackageManagerType()
        => Type.GetType("Windows.Management.Deployment.PackageManager, Microsoft.Windows.SDK.NET")
            ?? Type.GetType("Windows.Management.Deployment.PackageManager, Windows")
            ?? Type.GetType("Windows.Management.Deployment.PackageManager");

    private static string? GetRegistryString(RegistryKey key, string valueName)
        => key.GetValue(valueName)?.ToString()?.Trim();

    private static int GetRegistryInt(RegistryKey key, string valueName)
    {
        var value = key.GetValue(valueName);
        return value switch
        {
            int i => i,
            long l => (int)l,
            string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i,
            _ => 0,
        };
    }

    private static long? ToSizeBytes(object? value)
    {
        var sizeKb = value switch
        {
            int i => i,
            long l => l,
            string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => (long?)null,
        };

        return sizeKb.HasValue ? sizeKb.Value * 1024 : null;
    }

    private static DateTime? ParseRegistryDate(string? value)
        => DateTime.TryParseExact(
            value,
            "yyyyMMdd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out var parsed)
            ? parsed
            : null;

    private static DateTime? ParseWmiDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            return ManagementDateTimeConverter.ToDateTime(value);
        }
        catch (ArgumentOutOfRangeException)
        {
            // Win32_QuickFixEngineering often returns localized short dates instead of DMTF values.
        }

        return DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var parsed)
            || DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsed)
            ? parsed
            : null;
    }

    private static string? GetWmiString(ManagementBaseObject obj, string propertyName)
        => obj[propertyName]?.ToString()?.Trim();

    private static object? GetPropertyValue(object? instance, string propertyName)
        => instance?.GetType().GetProperty(propertyName)?.GetValue(instance);

    private static string? GetStringProperty(object? instance, string propertyName)
        => GetPropertyValue(instance, propertyName)?.ToString()?.Trim();

    private static bool GetBoolProperty(object? instance, string propertyName)
        => GetPropertyValue(instance, propertyName) is bool b && b;

    private static DateTime? GetDateTimeProperty(object? instance, string propertyName)
    {
        var value = GetPropertyValue(instance, propertyName);
        return value switch
        {
            DateTime dateTime => dateTime,
            DateTimeOffset dateTimeOffset => dateTimeOffset.LocalDateTime,
            _ => null,
        };
    }

    private static string? FormatPackageVersion(object? version)
    {
        if (version == null)
            return null;

        var major = GetPropertyValue(version, "Major");
        var minor = GetPropertyValue(version, "Minor");
        var build = GetPropertyValue(version, "Build");
        var revision = GetPropertyValue(version, "Revision");

        return major != null && minor != null && build != null && revision != null
            ? string.Create(CultureInfo.InvariantCulture, $"{major}.{minor}.{build}.{revision}")
            : version.ToString();
    }
}
