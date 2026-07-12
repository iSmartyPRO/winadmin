using System.Text.RegularExpressions;

namespace WinAdmin.Infrastructure.Software;

public enum SoftwareIdKind { Registry, Store, Update }

public static class SoftwareAppHelpers
{
    public const string RegistryPrefix = "reg:";
    public const string StorePrefix = "store:";
    public const string UpdatePrefix = "upd:";

    private static readonly HashSet<string> StoreSystemFamilies = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.Windows.ShellExperienceHost",
        "Microsoft.Windows.StartMenuExperienceHost",
        "Microsoft.Windows.Search",
        "Microsoft.Windows.Cortana",
        "Microsoft.AAD.BrokerPlugin",
        "Microsoft.AccountsControl",
        "Microsoft.Windows.CloudExperienceHost",
        "Microsoft.BioEnrollment",
        "Microsoft.LockApp",
        "Microsoft.Windows.ContentDeliveryManager",
        "windows.immersivecontrolpanel",
        "Microsoft.Windows.SecHealthUI",
        "Microsoft.MicrosoftEdge",
        "Microsoft.Win32WebViewHost",
        "Microsoft.XboxGameCallableUI",
        "Microsoft.XboxIdentityProvider",
    };

    public static string MakeRegistryId(string subKeyName) => RegistryPrefix + subKeyName;
    public static string MakeStoreId(string packageFullName) => StorePrefix + packageFullName;
    public static string MakeUpdateId(string hotfixId) => UpdatePrefix + hotfixId;

    public static bool TryParseId(string id, out SoftwareIdKind kind, out string key)
    {
        kind = default;
        key = "";
        if (string.IsNullOrWhiteSpace(id)) return false;
        if (id.StartsWith(RegistryPrefix, StringComparison.OrdinalIgnoreCase))
        {
            kind = SoftwareIdKind.Registry;
            key = id[RegistryPrefix.Length..];
            return key.Length > 0;
        }
        if (id.StartsWith(StorePrefix, StringComparison.OrdinalIgnoreCase))
        {
            kind = SoftwareIdKind.Store;
            key = id[StorePrefix.Length..];
            return key.Length > 0;
        }
        if (id.StartsWith(UpdatePrefix, StringComparison.OrdinalIgnoreCase))
        {
            kind = SoftwareIdKind.Update;
            key = id[UpdatePrefix.Length..];
            return key.Length > 0;
        }
        return false;
    }

    public static bool IsRegistrySystem(int systemComponent, string? parentDisplayNameOrKeyHint, string? displayName)
    {
        if (systemComponent == 1) return true;
        if (!string.IsNullOrEmpty(parentDisplayNameOrKeyHint) &&
            parentDisplayNameOrKeyHint.StartsWith("KB", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.IsNullOrEmpty(displayName) &&
            Regex.IsMatch(displayName, @"^Update for|Security Update for|Hotfix for", RegexOptions.IgnoreCase))
            return true;
        return false;
    }

    public static bool CanUninstallRegistry(string? uninstallString, string? quietUninstallString)
        => !string.IsNullOrWhiteSpace(quietUninstallString) || !string.IsNullOrWhiteSpace(uninstallString);

    public static string? BuildQuietUninstallCommand(string? uninstallString, string? quietUninstallString)
    {
        if (!string.IsNullOrWhiteSpace(quietUninstallString))
            return quietUninstallString.Trim();
        if (string.IsNullOrWhiteSpace(uninstallString))
            return null;
        var s = uninstallString.Trim();
        if (s.Contains("msiexec", StringComparison.OrdinalIgnoreCase))
        {
            if (!s.Contains("/qn", StringComparison.OrdinalIgnoreCase) &&
                !s.Contains("/quiet", StringComparison.OrdinalIgnoreCase))
                return s + " /qn /norestart";
            return s;
        }
        // Non-MSI: return as-is; many support /S - do not invent flags blindly
        return s;
    }

    public static bool IsStoreSystemPackage(string packageFamilyOrFullName)
    {
        var family = packageFamilyOrFullName;
        var us = family.IndexOf('_');
        // PackageFullName: Name_Version_Arch_Resource_PublisherId - family is Name_PublisherId
        // For simplicity: check if any known family is a prefix of the string or equals Name before first _
        foreach (var sys in StoreSystemFamilies)
        {
            if (family.StartsWith(sys + "_", StringComparison.OrdinalIgnoreCase) ||
                family.Equals(sys, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        // Framework / resource packages often contain ".NET.Native" or "Microsoft.NET."
        if (family.Contains("Microsoft.NET.", StringComparison.OrdinalIgnoreCase) ||
            family.Contains(".NET.Native", StringComparison.OrdinalIgnoreCase) ||
            family.Contains("Microsoft.VCLibs", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }
}
