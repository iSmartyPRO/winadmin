using WinAdmin.Core.Modules;

namespace WinAdmin.Core.Security;

/// <summary>Идентификаторы прав встроенных модулей и ядра.</summary>
public static class PermissionIds
{
    public const string SystemRead = "system.read";
    public const string ServicesRead = "services.read";
    public const string ServicesManage = "services.manage";
    public const string ProcessesRead = "processes.read";
    public const string ProcessesManage = "processes.manage";
    public const string PrintersRead = "printers.read";
    public const string PrintersManage = "printers.manage";
    public const string PowerManage = "power.manage";
    public const string EventLogsRead = "eventlogs.read";
    public const string EventLogsManage = "eventlogs.manage";
    public const string SoftwareRead = "software.read";
    public const string SoftwareManage = "software.manage";

    public const string AdUsersRead = "ad-users.read";
    public const string AdUsersEdit = "ad-users.edit";
    public const string AdUsersMove = "ad-users.move";
    public const string AdUsersPassword = "ad-users.password";
    public const string AdUsersOffboard = "ad-users.offboard";

    public const string PlatformUsersManage = "platform.users.manage";
    public const string PlatformRolesManage = "platform.roles.manage";
    public const string PlatformModulesManage = "platform.modules.manage";
    public const string PlatformApiKeysManage = "platform.apikeys.manage";
    public const string PlatformAuditRead = "platform.audit.read";
    public const string PlatformNetworkManage = "platform.network.manage";
    public const string PlatformDirectoryManage = "platform.directory.manage";
    public const string PlatformEnvironmentCheck = "platform.environment.check";

    /// <summary>Права ядра (не отключаются вместе с модулями).</summary>
    public static IReadOnlyList<PermissionDefinition> Platform { get; } =
    [
        new(PlatformUsersManage, "Пользователи WinAdmin", "Создание, отключение и пароли локальных пользователей", Dangerous: true),
        new(PlatformRolesManage, "Роли и назначения", "Роли и их выдача — в пределах собственных прав", Dangerous: true),
        new(PlatformModulesManage, "Модули", "Включение, отключение и настройка модулей", Dangerous: true),
        new(PlatformApiKeysManage, "API-ключи", "Выпуск и отзыв API-ключей", Dangerous: true),
        new(PlatformAuditRead, "Журнал аудита", "Просмотр журнала действий"),
        new(PlatformNetworkManage, "Сетевой доступ", "Порт, режим доступа, разрешённые подсети", Dangerous: true),
        new(PlatformDirectoryManage, "Подключение к домену", "Настройки подключения к Active Directory", Dangerous: true),
        new(PlatformEnvironmentCheck, "Проверка окружения", "Готовность домена, учётки записи и модулей"),
    ];
}
