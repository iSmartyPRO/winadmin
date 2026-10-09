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
        HardenDirectory(appDirectory, AclPlan.ForAppDirectory(), logger);
        if (AclPlan.SameDirectory(appDirectory, dataDirectory))
            logger.LogWarning("БД лежит в папке приложения ({Dir}) и доступна пользователям на чтение. " +
                              "Вынесите её в C:\\ProgramData\\WinAdmin (WinAdmin__DatabasePath).", dataDirectory);
        else
            HardenDirectory(dataDirectory, AclPlan.ForDataDirectory(), logger);
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

        foreach (var item in root.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
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
