using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure.Access;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Tests;

public sealed class PlatformBootstrapperTests : IDisposable
{
    private static readonly PermissionCatalog Catalog = new(BuiltInModules.All);
    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private readonly SqliteWinAdminDbContext _db;

    public PlatformBootstrapperTests()
    {
        _connection.Open();
        _db = new SqliteWinAdminDbContext(new DbContextOptionsBuilder<SqliteWinAdminDbContext>().UseSqlite(_connection).Options);
        _db.Database.Migrate();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task Existing_admins_become_administrators_and_rerun_changes_nothing()
    {
        foreach (var login in new[] { "admin", "iadmin", "iadmin1" })
            _db.Users.Add(new UserEntity { Id = login, Login = login, PasswordHash = "x", Scopes = "admin" });
        _db.ApiKeys.Add(new ApiKeyEntity { Id = "boot", Name = "bootstrap-admin", KeyHash = "h", Scopes = "admin" });
        await _db.SaveChangesAsync();

        await PlatformBootstrapper.RunAsync(_db, Catalog);
        await PlatformBootstrapper.RunAsync(_db, Catalog);

        var admins = await _db.RoleAssignments.Where(a => a.RoleId == BuiltInRoles.AdministratorId).ToListAsync();
        Assert.Equal(4, admins.Count);
        Assert.Equal(1, await _db.Roles.CountAsync(r => r.Id == BuiltInRoles.AdministratorId && r.IsBuiltin));
        Assert.All(await _db.Users.ToListAsync(), u => Assert.Equal("", u.Scopes));
        Assert.Equal("", (await _db.ApiKeys.SingleAsync()).Scopes);
    }

    [Fact]
    public async Task Same_scope_sets_share_one_imported_role()
    {
        _db.Users.Add(new UserEntity { Id = "a", Login = "a", PasswordHash = "x", Scopes = "services.read,disks.read" });
        _db.ApiKeys.Add(new ApiKeyEntity { Id = "k", Name = "mon", KeyHash = "h", Scopes = "disks.read, services.read,bogus.scope" });
        await _db.SaveChangesAsync();

        await PlatformBootstrapper.RunAsync(_db, Catalog);

        var imported = await _db.Roles.Include(r => r.Permissions).SingleAsync(r => !r.IsBuiltin);
        Assert.Equal("Импорт: services.read, system.read", imported.Name);
        Assert.Equal(new[] { "services.read", "system.read" }, imported.Permissions.Select(p => p.PermissionId).Order());
        Assert.Equal(2, await _db.RoleAssignments.CountAsync(a => a.RoleId == imported.Id));
    }

    [Fact]
    public async Task Unknown_only_scopes_assign_nothing_but_are_cleared()
    {
        _db.Users.Add(new UserEntity { Id = "z", Login = "z", PasswordHash = "x", Scopes = "bogus.scope" });
        await _db.SaveChangesAsync();

        await PlatformBootstrapper.RunAsync(_db, Catalog);

        Assert.Empty(await _db.RoleAssignments.ToListAsync());
        Assert.Equal("", (await _db.Users.SingleAsync()).Scopes);
    }
}
