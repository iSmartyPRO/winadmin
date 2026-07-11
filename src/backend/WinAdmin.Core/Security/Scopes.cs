namespace WinAdmin.Core.Security;

/// <summary>
/// Гранулярные права доступа (scopes), назначаемые API-ключам.
/// *.read — чтение информации, *.manage — управляющие действия.
/// </summary>
public static class Scopes
{
    public const string SystemRead = "system.read";
    public const string DisksRead = "disks.read";
    public const string ServicesRead = "services.read";
    public const string ServicesManage = "services.manage";
    public const string ProcessesRead = "processes.read";
    public const string ProcessesManage = "processes.manage";
    public const string PrintersRead = "printers.read";
    public const string PrintersManage = "printers.manage";
    public const string PowerManage = "power.manage";
    public const string EventLogsRead = "eventlogs.read";

    /// <summary>Управление API-ключами и просмотр аудита.</summary>
    public const string Admin = "admin";

    /// <summary>Полный список известных scopes (для валидации и UI).</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        SystemRead, DisksRead,
        ServicesRead, ServicesManage,
        ProcessesRead, ProcessesManage,
        PrintersRead, PrintersManage,
        PowerManage, EventLogsRead, Admin,
    };

    public static bool IsValid(string scope) => All.Contains(scope);
}
