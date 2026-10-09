using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Core.Security;

namespace WinAdmin.Core.Modules;

/// <summary>Модуль из существующего раздела: без настроек и областей, сервисы регистрируются в Infrastructure.</summary>
public sealed class BuiltInModule(string id, string title, string description, params PermissionDefinition[] permissions)
    : IWinAdminModule
{
    public string Id => id;
    public string Title => title;
    public string? Description => description;
    public ModuleRequirements Requirements => ModuleRequirements.None;
    public bool EnabledByDefault => true;
    public IReadOnlyList<PermissionDefinition> Permissions => permissions;
    public Type? SettingsType => null;
    public IScopeProvider? Scope => null;
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration) { }
}

public static class BuiltInModules
{
    public static IReadOnlyList<IWinAdminModule> All { get; } =
    [
        new BuiltInModule("system", "Система", "Дашборд, сведения о системе и диски",
            new PermissionDefinition(PermissionIds.SystemRead, "Просмотр системы и дисков")),
        new BuiltInModule("services", "Службы", "Службы Windows",
            new PermissionDefinition(PermissionIds.ServicesRead, "Просмотр служб"),
            new PermissionDefinition(PermissionIds.ServicesManage, "Запуск, остановка и перезапуск служб", Dangerous: true)),
        new BuiltInModule("processes", "Процессы", "Запущенные процессы",
            new PermissionDefinition(PermissionIds.ProcessesRead, "Просмотр процессов"),
            new PermissionDefinition(PermissionIds.ProcessesManage, "Завершение процессов", Dangerous: true)),
        new BuiltInModule("printers", "Принтеры", "Принтеры и очереди печати",
            new PermissionDefinition(PermissionIds.PrintersRead, "Просмотр принтеров"),
            new PermissionDefinition(PermissionIds.PrintersManage, "Пауза, возобновление, очистка очереди")),
        new BuiltInModule("power", "Питание", "Перезагрузка и выключение",
            new PermissionDefinition(PermissionIds.PowerManage, "Перезагрузка и выключение машины", Dangerous: true)),
        new BuiltInModule("eventlogs", "Журналы Windows", "Журналы событий Windows",
            new PermissionDefinition(PermissionIds.EventLogsRead, "Просмотр журналов"),
            new PermissionDefinition(PermissionIds.EventLogsManage, "Исключения учётных записей в журналах")),
        new BuiltInModule("software", "Программы", "Установленные программы и обновления",
            new PermissionDefinition(PermissionIds.SoftwareRead, "Просмотр программ и обновлений"),
            new PermissionDefinition(PermissionIds.SoftwareManage, "Удаление программ и откат обновлений", Dangerous: true)),
        new ActiveDirectory.Users.AdUsersModule(),
    ];
}
