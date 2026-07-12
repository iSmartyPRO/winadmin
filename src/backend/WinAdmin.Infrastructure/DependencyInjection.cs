using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
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
        string sqliteConnectionString,
        JwtOptions jwtOptions)
    {
        services.AddDbContext<WinAdminDbContext>(o => o.UseSqlite(sqliteConnectionString));

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

        return services;
    }
}
