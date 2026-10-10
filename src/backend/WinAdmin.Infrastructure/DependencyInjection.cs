using WinAdmin.Infrastructure.ActiveDirectory.Users;
using WinAdmin.Core.EnvironmentChecks;
using WinAdmin.Infrastructure.EnvironmentChecks;
using WinAdmin.Infrastructure.ActiveDirectory;
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Infrastructure.Modules;
using WinAdmin.Infrastructure.Access;
using WinAdmin.Core.Modules;
using WinAdmin.Infrastructure.Disks;
using WinAdmin.Infrastructure.EventLogs;
using WinAdmin.Infrastructure.Power;
using WinAdmin.Infrastructure.Printers;
using WinAdmin.Infrastructure.Processes;
using WinAdmin.Infrastructure.Security;
using WinAdmin.Infrastructure.Services;
using WinAdmin.Infrastructure.Settings;
using WinAdmin.Infrastructure.Software;
using WinAdmin.Infrastructure.Storage;
using WinAdmin.Infrastructure.MachineInfo;

namespace WinAdmin.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Регистрирует сбор системной информации, действия и хранилище.</summary>
    public static IServiceCollection AddWinAdminInfrastructure(
        this IServiceCollection services,
        DatabaseSettings database,
        JwtOptions jwtOptions)
    {
        services.AddWinAdminDatabase(database);

        services.AddSingleton(jwtOptions);

        // singleton — держит счётчики производительности между запросами
        services.AddSingleton<ISystemInfoService, SystemInfoService>();

        services.AddScoped<IDiskService, DiskService>();
        services.AddScoped<IServiceControlService, ServiceControlService>();
        services.AddScoped<IProcessService, ProcessService>();
        services.AddScoped<IPrinterService, PrinterService>();
        services.AddScoped<IPowerService, PowerService>();
        services.AddScoped<IEventLogService, EventLogService>();
        services.AddScoped<ISoftwareCatalogService, SoftwareCatalogService>();
        services.AddSingleton<ISoftwareProcessRunner, SoftwareProcessRunner>();
        services.AddSingleton<ISoftwareJobService, SoftwareJobService>();

        services.AddScoped<IApiKeyService, ApiKeyService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IExcludedUserService, ExcludedUserService>();

        services.AddSingleton(new PermissionCatalog(BuiltInModules.All));
        services.AddSingleton<IAccessService, AccessService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddSingleton<IMachineInfo, WmiMachineInfo>();
        services.AddSingleton<IDirectorySettingsStore, DirectorySettingsStore>();
        services.AddSingleton<IAdStructureStore, AdStructureStore>();
        services.AddSingleton<IAdWriter, LdapAdWriter>();
        services.AddScoped<IAdUsersService, AdUsersService>();
        services.AddSingleton<WinAdmin.Infrastructure.ActiveDirectory.Folders.FolderCatalogCache>();
        services.AddScoped<IAdFoldersService, WinAdmin.Infrastructure.ActiveDirectory.Folders.AdFoldersService>();
        services.AddSingleton<INtfsAccess, WinAdmin.Infrastructure.ActiveDirectory.Folders.NtfsAccess>();
        services.AddSingleton<IAdFolderDirectory>(sp => new WinAdmin.Infrastructure.ActiveDirectory.Folders.LdapAdFolderDirectory(sp.GetRequiredService<IDirectorySettingsStore>()));
        services.AddSingleton<IAdUserDirectory>(sp => new LdapAdUserDirectory(
            sp.GetRequiredService<IDirectorySettingsStore>(), sp.GetRequiredService<IAdStructureStore>()));
        services.AddSingleton<IEnvironmentService, EnvironmentService>();
        services.AddSingleton<IEnvironmentCheck, PlatformAdCheck>();
        services.AddSingleton<IEnvironmentCheck, AdUsersCheck>();
        services.AddSingleton<IAdReader>(sp => new LdapAdReader(
            sp.GetRequiredService<IDirectorySettingsStore>(), sp.GetRequiredService<IAdStructureStore>()));
        services.AddSingleton<IAdGroupCache, AdGroupCache>();
        services.AddScoped<IDirectorySignIn, DirectorySignInService>();
        services.AddSingleton<IDirectoryService>(sp => new LdapDirectoryService(sp.GetRequiredService<IDirectorySettingsStore>()));
        services.AddSingleton<IModuleRegistry, ModuleRegistry>();

        return services;
    }
}
