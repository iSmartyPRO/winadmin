using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.ActiveDirectory.Folders;
using WinAdmin.Core.ActiveDirectory.Users;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure.Access;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Tests;

public sealed class AdRoleTemplatesTests : IDisposable
{
    private static readonly PermissionCatalog Catalog = new(BuiltInModules.All);
    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private readonly SqliteWinAdminDbContext _db;
    private readonly RoleService _roles;

    public AdRoleTemplatesTests()
    {
        _connection.Open();
        _db = new SqliteWinAdminDbContext(new DbContextOptionsBuilder<SqliteWinAdminDbContext>().UseSqlite(_connection).Options);
        _db.Database.Migrate();
        _roles = new RoleService(_db, Catalog, Mock.Of<IAccessService>(), Mock.Of<IAuditService>());
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task Creates_all_ad_templates_once_and_reports_created_names()
    {
        var created = await AdRoleTemplates.EnsureAsync(_roles, Catalog, AdRoleTemplates.All);
        var again = await AdRoleTemplates.EnsureAsync(_roles, Catalog, AdRoleTemplates.All);

        Assert.Equal([AdFoldersModule.AdminRoleName, AdUsersModule.AdminRoleName, AdUsersModule.HrRoleName], created.Order());
        Assert.Empty(again);
        var roles = await _roles.ListAsync();
        Assert.Equal([PermissionIds.AdUsersEdit, PermissionIds.AdUsersMove, PermissionIds.AdUsersRead],
            roles.Single(r => r.Name == AdUsersModule.HrRoleName).Permissions.Select(p => p.PermissionId).Order());
        Assert.Equal(3, roles.Single(r => r.Name == AdFoldersModule.AdminRoleName).Permissions.Count);
        Assert.Equal(5, roles.Single(r => r.Name == AdUsersModule.AdminRoleName).Permissions.Count);
    }

    [Fact]
    public void Admin_templates_cover_every_module_permission()
    {
        Assert.Equal(new AdUsersModule().Permissions.Select(p => p.Id).Order(),
            AdRoleTemplates.Users.Single(t => t.Name == AdUsersModule.AdminRoleName).Permissions.Select(p => p.PermissionId).Order());
        Assert.Equal(new AdFoldersModule().Permissions.Select(p => p.Id).Order(),
            AdRoleTemplates.Folders.Single().Permissions.Select(p => p.PermissionId).Order());
    }

    [Fact]
    public async Task Template_names_are_matched_case_insensitively()
    {
        await AdRoleTemplates.EnsureAsync(_roles, Catalog, AdRoleTemplates.Users);
        var created = await AdRoleTemplates.EnsureAsync(_roles, Catalog,
            [AdRoleTemplates.Folders[0], AdRoleTemplates.Users[0] with { Name = AdRoleTemplates.Users[0].Name.ToUpperInvariant() }]);

        Assert.Equal([AdFoldersModule.AdminRoleName], created);
    }
}
