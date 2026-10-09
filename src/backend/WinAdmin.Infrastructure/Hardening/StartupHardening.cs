using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using WinAdmin.Infrastructure.Secrets;

namespace WinAdmin.Infrastructure.Hardening;

/// <summary>
/// Права при старте службы: папки приложения и данных, затем — заново — каталог ключей и
/// bootstrap-key.txt. Исправление папок сбрасывает вложенные файлы к наследованию (в папке
/// приложения пользователи могут читать), поэтому секреты закрываются после него.
/// </summary>
public static class StartupHardening
{
    public static void Run(string appDirectory, string dataDirectory, MasterKeyStore keys, string bootstrapKeyFile, ILogger logger)
    {
        InstallationHardening.Apply(appDirectory, dataDirectory, logger);
        if (Directory.Exists(keys.KeysDirectory))
            keys.ProtectKeysDirectory(logger);
        if (File.Exists(bootstrapKeyFile))
            InstallationHardening.ProtectFile(bootstrapKeyFile, BootstrapKeyRules(), logger);
    }

    /// <summary>Admin API-ключ: только администраторы, SYSTEM и процесс, который его создал.</summary>
    public static IReadOnlyList<AclRule> BootstrapKeyRules() =>
    [
        .. AclPlan.ForDataDirectory(),
        new AclRule(WindowsIdentity.GetCurrent().User!.Value, FileSystemRights.FullControl),
    ];
}
