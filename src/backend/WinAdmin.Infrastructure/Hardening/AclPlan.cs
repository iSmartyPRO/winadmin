using System.Security.AccessControl;

namespace WinAdmin.Infrastructure.Hardening;

/// <summary>Разрешение для SID (SID, а не имя группы — имена локализованы).</summary>
public sealed record AclRule(string Sid, FileSystemRights Rights);

/// <summary>Наборы прав для папок WinAdmin.</summary>
public static class AclPlan
{
    public const string Administrators = "S-1-5-32-544";
    public const string LocalSystem = "S-1-5-18";
    public const string Users = "S-1-5-32-545";

    /// <summary>Папка приложения: служба работает как SYSTEM, поэтому писать сюда могут только админы.</summary>
    public static IReadOnlyList<AclRule> ForAppDirectory() =>
    [
        new(Administrators, FileSystemRights.FullControl),
        new(LocalSystem, FileSystemRights.FullControl),
        new(Users, FileSystemRights.ReadAndExecute),
    ];

    /// <summary>Папка данных: БД (хеши паролей, ключи), network.json — только админы и SYSTEM.</summary>
    public static IReadOnlyList<AclRule> ForDataDirectory() =>
    [
        new(Administrators, FileSystemRights.FullControl),
        new(LocalSystem, FileSystemRights.FullControl),
    ];

    public static bool SameDirectory(string a, string b)
        => string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            StringComparison.OrdinalIgnoreCase);
}
