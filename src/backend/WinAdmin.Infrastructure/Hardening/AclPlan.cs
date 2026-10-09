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

    /// <summary>
    /// Можно ли менять права на папку: не корень диска, не общая системная папка и в ней
    /// есть хотя бы один из файлов WinAdmin (защита от неверного пути — служба работает как SYSTEM).
    /// </summary>
    public static bool IsSafeTarget(string directory, IReadOnlyList<string> markerFiles)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        string? root = Path.GetPathRoot(full);
        if (root is null || SameDirectory(full, root))
            return false;

        Environment.SpecialFolder[] shared =
        [
            Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolder.ProgramFiles,
            Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.Windows,
            Environment.SpecialFolder.System, Environment.SpecialFolder.UserProfile,
        ];
        foreach (var folder in shared)
        {
            string path = Environment.GetFolderPath(folder);
            if (path.Length > 0 && SameDirectory(full, path))
                return false;
        }
        string users = Path.Combine(root, "Users");
        if (SameDirectory(full, users) || SameDirectory(Path.GetDirectoryName(full) ?? "", users))
            return false;

        return markerFiles.Any(m => File.Exists(Path.Combine(full, m)));
    }

    public static bool SameDirectory(string a, string b)
        => string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            StringComparison.OrdinalIgnoreCase);
}
