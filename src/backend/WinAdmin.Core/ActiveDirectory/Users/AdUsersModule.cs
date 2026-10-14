using System.ComponentModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;

namespace WinAdmin.Core.ActiveDirectory.Users;

public sealed class AdUsersSettings
{
    public static IReadOnlyList<string> DefaultAttributes { get; } =
    [
        "displayName", "givenName", "sn", "mail", "department", "title",
        "telephoneNumber", "physicalDeliveryOfficeName", "description", "company",
    ];

    [Description("Группа уволенных (имя или DN)")]
    public string FiredGroup { get; set; } = "";

    [Description("OU уволенных (DN)")]
    public string TerminatedOuDn { get; set; } = "";

    [Description("Редактируемые атрибуты")]
    public List<string> EditableAttributes { get; set; } = [.. DefaultAttributes];

    [Description("Максимальный размер фото, КБ")]
    public int PhotoMaxKb { get; set; } = 100;
}

/// <summary>Пользователи Active Directory: просмотр, атрибуты, фото, перенос, пароль, увольнение.</summary>
public sealed class AdUsersModule : IWinAdminModule, IModuleLifecycle
{
    public const string ModuleId = "ad-users";
    public const string HrRoleName = "AD: отдел кадров";
    public const string AdminRoleName = "AD: администраторы пользователей";

    public string Id => ModuleId;
    public string Title => "Пользователи AD";
    public string? Description => "Пользователи Active Directory по проектам: атрибуты, фото, перенос, пароль, увольнение";
    public ModuleRequirements Requirements => ModuleRequirements.DomainJoined;
    public bool EnabledByDefault => false;
    public Type? SettingsType => typeof(AdUsersSettings);
    public IScopeProvider? Scope { get; } = new ProjectScopeProvider();

    public IReadOnlyList<PermissionDefinition> Permissions { get; } =
    [
        new(PermissionIds.AdUsersRead, "Просмотр пользователей", "Список, карточка, группы, история", Scopable: true),
        new(PermissionIds.AdUsersEdit, "Атрибуты и фото", "ФИО, почта, отдел, должность, телефон, офис, описание, компания, фото", Scopable: true),
        new(PermissionIds.AdUsersMove, "Перенос между проектами", Scopable: true),
        new(PermissionIds.AdUsersPassword, "Сброс пароля", Scopable: true, Dangerous: true),
        new(PermissionIds.AdUsersOffboard, "Увольнение и восстановление", Scopable: true, Dangerous: true),
    ];

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration) { }

    public Task OnFirstEnabledAsync(IServiceProvider services, CancellationToken ct)
        => AdRoleTemplates.EnsureAsync(services.GetRequiredService<IRoleService>(), services.GetRequiredService<PermissionCatalog>(),
            AdRoleTemplates.Users, ct);
}
