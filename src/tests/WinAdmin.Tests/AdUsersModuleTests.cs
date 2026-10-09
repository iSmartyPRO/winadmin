using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.ActiveDirectory.Users;
using WinAdmin.Core.Models;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure.Access;
using WinAdmin.Infrastructure.Modules;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Tests;

public sealed class AdUsersModuleTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private readonly ServiceProvider _sp;

    public AdUsersModuleTests()
    {
        _connection.Open();
        var catalog = new PermissionCatalog(BuiltInModules.All);
        var services = new ServiceCollection()
            .AddDbContext<WinAdminDbContext, SqliteWinAdminDbContext>(o => o.UseSqlite(_connection))
            .AddSingleton(catalog)
            .AddSingleton(Mock.Of<IAccessService>())
            .AddSingleton(Mock.Of<IAuditService>())
            .AddScoped<IRoleService, RoleService>()
            .AddSingleton(Mock.Of<IMachineInfo>(m => m.IsDomainJoined == true))
            .AddSingleton(Mock.Of<ISecretProtector>())
            .AddSingleton<IModuleRegistry, ModuleRegistry>();
        _sp = services.BuildServiceProvider();
        using var scope = _sp.CreateScope();
        scope.ServiceProvider.GetRequiredService<WinAdminDbContext>().Database.Migrate();
    }

    public void Dispose()
    {
        _sp.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public void Module_declares_scoped_permissions_settings_and_domain_requirement()
    {
        var module = Assert.Single(BuiltInModules.All, m => m.Id == AdUsersModule.ModuleId);
        Assert.Equal(ModuleRequirements.DomainJoined, module.Requirements);
        Assert.False(module.EnabledByDefault);
        Assert.IsType<ProjectScopeProvider>(module.Scope);
        Assert.Equal(typeof(AdUsersSettings), module.SettingsType);
        Assert.All(module.Permissions, p => Assert.True(p.Scopable));
        Assert.True(module.Permissions.Single(p => p.Id == PermissionIds.AdUsersPassword).Dangerous);
        Assert.True(module.Permissions.Single(p => p.Id == PermissionIds.AdUsersOffboard).Dangerous);
        Assert.Equal(10, new AdUsersSettings().EditableAttributes.Count);
    }

    [Fact]
    public async Task First_enable_creates_role_templates_once()
    {
        var registry = _sp.GetRequiredService<IModuleRegistry>();
        await registry.SetEnabledAsync(AdUsersModule.ModuleId, true, "test");
        await registry.SetEnabledAsync(AdUsersModule.ModuleId, false, "test");
        await registry.SetEnabledAsync(AdUsersModule.ModuleId, true, "test");

        using var scope = _sp.CreateScope();
        var roles = await scope.ServiceProvider.GetRequiredService<IRoleService>().ListAsync();
        var hr = Assert.Single(roles, r => r.Name == AdUsersModule.HrRoleName);
        Assert.Equal([PermissionIds.AdUsersEdit, PermissionIds.AdUsersMove, PermissionIds.AdUsersRead],
            hr.Permissions.Select(p => p.PermissionId).Order());
        Assert.Single(roles, r => r.Name == AdUsersModule.AdminRoleName);
    }

    [Fact]
    public async Task Existing_role_with_template_name_is_left_alone()
    {
        using (var scope = _sp.CreateScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<IRoleService>();
            var catalog = scope.ServiceProvider.GetRequiredService<PermissionCatalog>();
            var system = new AccessContext(new PrincipalRef(PrincipalType.LocalUser, "t", []), "t",
                PermissionEvaluator.Evaluate([new RoleSnapshot(BuiltInRoles.AdministratorId, true, [])], catalog));
            await roles.CreateAsync(new SaveRoleRequest(AdUsersModule.HrRoleName, "своя", [new RoleGrantDto(PermissionIds.AdUsersRead, null)]), system);
        }
        await _sp.GetRequiredService<IModuleRegistry>().SetEnabledAsync(AdUsersModule.ModuleId, true, "test");

        using var check = _sp.CreateScope();
        var hr = (await check.ServiceProvider.GetRequiredService<IRoleService>().ListAsync()).Single(r => r.Name == AdUsersModule.HrRoleName);
        Assert.Equal("своя", hr.Description);
    }
}
