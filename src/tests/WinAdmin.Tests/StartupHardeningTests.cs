using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Logging.Abstractions;
using WinAdmin.Infrastructure.Hardening;
using WinAdmin.Infrastructure.Secrets;

namespace WinAdmin.Tests;

public sealed class StartupHardeningTests : IDisposable
{
    private readonly string _app = Path.Combine(Path.GetTempPath(), "winadmin-sh-" + Guid.NewGuid().ToString("N"));

    public StartupHardeningTests()
    {
        Directory.CreateDirectory(_app);
        File.WriteAllText(Path.Combine(_app, "WinAdmin.exe"), "marker");
    }

    public void Dispose()
    {
        // После исправления прав у тестового пользователя нет записи — владелец возвращает себе доступ.
        var me = WindowsIdentity.GetCurrent().User!.Value;
        InstallationHardening.HardenDirectory(_app,
            [new AclRule(me, FileSystemRights.FullControl)], NullLogger.Instance);
        Directory.Delete(_app, recursive: true);
    }

    private static bool UsersCanRead(string file) =>
        new FileInfo(file).GetAccessControl()
            .GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
            .Any(r => r.IdentityReference.Value is AclPlan.Users or "S-1-5-11" && r.AccessControlType == AccessControlType.Allow);

    [Fact]
    public void Keys_and_bootstrap_key_stay_private_when_database_lives_next_to_exe()
    {
        // БД рядом с exe: каталог данных = папка приложения, keys\ — внутри неё.
        var keys = new MasterKeyStore(Path.Combine(_app, "keys"));
        keys.LoadOrCreate();
        string bootstrap = Path.Combine(_app, "bootstrap-key.txt");
        File.WriteAllText(bootstrap, "sp_admin_key");

        // Само по себе исправление прав на папку приложения открывает ключ пользователям (механизм проблемы).
        InstallationHardening.Apply(_app, _app, NullLogger.Instance);
        Assert.True(UsersCanRead(keys.FilePath));

        StartupHardening.Run(_app, _app, keys, bootstrap, NullLogger.Instance);

        Assert.False(UsersCanRead(keys.FilePath));
        Assert.False(UsersCanRead(bootstrap));
        Assert.Equal(32, keys.Load().Length); // процесс по-прежнему читает ключ
    }
}
