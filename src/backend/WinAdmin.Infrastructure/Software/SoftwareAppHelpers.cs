using System.Text.RegularExpressions;
using WinAdmin.Core.Models;

namespace WinAdmin.Infrastructure.Software;

public enum SoftwareIdKind { Registry, Store, Update }

public static class SoftwareAppHelpers
{
    public const string RegistryPrefix = "reg:";
    public const string StorePrefix = "store:";
    public const string UpdatePrefix = "upd:";
    private static readonly string[] ExecutableExtensions = [".exe", ".msi", ".bat", ".cmd"];

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

    public static (string FileName, string Arguments) SplitCommand(string command)
    {
        var trimmed = command.Trim();
        if (trimmed.Length == 0)
            throw new SoftwareActionNotAllowedException("Uninstall command is empty.");

        if (trimmed[0] == '"')
        {
            var endQuote = trimmed.IndexOf('"', 1);
            if (endQuote < 0)
                return (trimmed.Trim('"'), string.Empty);

            return (trimmed[1..endQuote], trimmed[(endQuote + 1)..].TrimStart());
        }

        var executablePath = FindExistingExecutablePath(trimmed);
        if (executablePath is not null)
            return (executablePath, trimmed[executablePath.Length..].TrimStart());

        var split = trimmed.IndexOf(' ');
        return split < 0
            ? (trimmed, string.Empty)
            : (trimmed[..split], trimmed[(split + 1)..].TrimStart());
    }

    private static string? FindExistingExecutablePath(string command)
    {
        foreach (var boundary in GetTokenBoundaries(command))
        {
            var candidate = command[..boundary];
            if (ExecutableExtensions.Contains(Path.GetExtension(candidate), StringComparer.OrdinalIgnoreCase) &&
                File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static IEnumerable<int> GetTokenBoundaries(string command)
    {
        for (var i = 0; i < command.Length; i++)
        {
            if (char.IsWhiteSpace(command[i]) && i > 0)
                yield return i;
        }

        yield return command.Length;
    }

    public static bool IsStoreSystemPackage(string packageFamilyOrFullName)
    {
        var family = packageFamilyOrFullName;
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
