using System.ComponentModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;

namespace WinAdmin.Core.ActiveDirectory.Folders;

public sealed class AdFoldersSettings
{
    [Description("Префикс групп доступа к папкам")]
    public string GroupPrefix { get; set; } = "sg_";

    [Description("OU для новых групп внутри проекта (пусто — сама OU проекта)")]
    public string GroupsOuName { get; set; } = "";

    [Description(@"Буквы дисков → UNC, по строке: A=\\fs01\Projects")]
    public List<string> DriveMappings { get; set; } = [];

    [Description("NTFS-права группы Full (Modify, FullControl, …)")]
    public string FullRights { get; set; } = "Modify";

    [Description("NTFS-права группы Read (ReadAndExecute, Read, …)")]
    public string ReadRights { get; set; } = "ReadAndExecute";
}

/// <summary>Доступ к сетевым папкам через группы безопасности (логика Access).</summary>
public sealed class AdFoldersModule : IWinAdminModule, IModuleLifecycle
{
    public const string ModuleId = "ad-folders";
    public const string AdminRoleName = "AD: администраторы папок";

    public string Id => ModuleId;
    public string Title => "Папки";
    public string? Description => "Доступ к сетевым папкам через группы безопасности: участники Full/Read, новые папки, права NTFS";
    public ModuleRequirements Requirements => ModuleRequirements.DomainJoined;
    public bool EnabledByDefault => false;
    public Type? SettingsType => typeof(AdFoldersSettings);
    public IScopeProvider? Scope { get; } = new ProjectScopeProvider();

    public IReadOnlyList<PermissionDefinition> Permissions { get; } =
    [
        new(PermissionIds.AdFoldersRead, "Просмотр папок", "Каталог папок, участники, доступы пользователя", Scopable: true),
        new(PermissionIds.AdFoldersMembership, "Участники папок", "Добавить или убрать участника в Full/Read", Scopable: true),
        new(PermissionIds.AdFoldersCreate, "Новые папки и права NTFS", "Создание групп и выставление прав на папке", Scopable: true, Dangerous: true),
    ];

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration) { }

    public async Task OnFirstEnabledAsync(IServiceProvider services, CancellationToken ct)
    {
        var roles = services.GetRequiredService<IRoleService>();
        var catalog = services.GetRequiredService<PermissionCatalog>();
        if ((await roles.ListAsync(ct)).Any(r => string.Equals(r.Name, AdminRoleName, StringComparison.CurrentCultureIgnoreCase)))
            return;
        var system = new AccessContext(new PrincipalRef(PrincipalType.LocalUser, "system", []), "system",
            PermissionEvaluator.Evaluate([new RoleSnapshot(BuiltInRoles.AdministratorId, true, [])], catalog));
        await roles.CreateAsync(new SaveRoleRequest(AdminRoleName, "Шаблон: все действия с папками",
            Permissions.Select(p => new RoleGrantDto(p.Id, null)).ToList()), system, ct);
    }
}
