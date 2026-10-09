using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Logging;

namespace WinAdmin.Infrastructure.Hardening;

/// <summary>
/// Приводит права на папки приложения и данных к безопасным (вызывается при старте службы).
/// Без этого любой пользователь мог подменить exe службы LocalSystem или прочитать БД.
/// </summary>
public static class InstallationHardening
{
    public static void Apply(string appDirectory, string dataDirectory, ILogger logger)
    {
        if (AclPlan.IsSafeTarget(appDirectory, ["WinAdmin.exe"]))
            HardenDirectory(appDirectory, AclPlan.ForAppDirectory(), logger);
        else
            logger.LogWarning("Права на {Dir} не изменены: похоже на общую папку, а не на папку WinAdmin.", appDirectory);

        if (AclPlan.SameDirectory(appDirectory, dataDirectory))
            logger.LogWarning("БД лежит в папке приложения ({Dir}) и доступна пользователям на чтение. " +
                              "Вынесите её в C:\\ProgramData\\WinAdmin (WinAdmin__DatabasePath).", dataDirectory);
        else if (AclPlan.IsSafeTarget(dataDirectory, ["WinAdmin.db", WinAdminPaths.NetworkFileName]))
            HardenDirectory(dataDirectory, AclPlan.ForDataDirectory(), logger);
        else
            logger.LogWarning("Права на {Dir} не изменены: похоже на общую папку, а не на папку данных WinAdmin. " +
                              "Укажите отдельную папку в WinAdmin__DatabasePath.", dataDirectory);
    }

    /// <summary>Ставит на файл защищённый DACL из rules (например, bootstrap-key.txt — admin-ключ).</summary>
    public static void ProtectFile(string path, IReadOnlyList<AclRule> rules, ILogger logger)
    {
        try
        {
            var security = new FileSecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            foreach (var rule in rules)
                security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(rule.Sid), rule.Rights, AccessControlType.Allow));
            new FileInfo(path).SetAccessControl(security);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PrivilegeNotHeldException)
        {
            logger.LogWarning(ex, "Не удалось ограничить права на {File}.", path);
        }
    }

    /// <summary>
    /// Ставит на каталог защищённый DACL из rules (с наследованием) и сбрасывает
    /// явные разрешения у вложенных файлов/папок, чтобы они наследовали только его.
    /// </summary>
    public static void HardenDirectory(string directory, IReadOnlyList<AclRule> rules, ILogger logger)
    {
        var root = new DirectoryInfo(directory);
        if (!root.Exists) return;
        try
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            foreach (var rule in rules)
            {
                security.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(rule.Sid), rule.Rights,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None, AccessControlType.Allow));
            }
            root.SetAccessControl(security);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PrivilegeNotHeldException)
        {
            logger.LogWarning(ex, "Не удалось исправить права на {Dir}.", directory);
            return;
        }

        // Не заходим в недоступные каталоги и не следуем по junction/symlink — иначе SYSTEM
        // сбросил бы права на чужие папки, а исключение в обходе уронило бы старт службы.
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        IEnumerable<FileSystemInfo> items;
        try
        {
            items = root.EnumerateFileSystemInfos("*", options).ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            logger.LogWarning(ex, "Не удалось обойти {Dir} для сброса прав.", directory);
            return;
        }

        foreach (var item in items)
        {
            try
            {
                switch (item)
                {
                    case DirectoryInfo d:
                        var ds = new DirectorySecurity();
                        ds.SetAccessRuleProtection(isProtected: false, preserveInheritance: false);
                        d.SetAccessControl(ds);
                        break;
                    case FileInfo f:
                        var fs = new FileSecurity();
                        fs.SetAccessRuleProtection(isProtected: false, preserveInheritance: false);
                        f.SetAccessControl(fs);
                        break;
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PrivilegeNotHeldException)
            {
                logger.LogWarning("Не удалось сбросить права на {Path}: {Message}", item.FullName, ex.Message);
            }
        }
    }
}
