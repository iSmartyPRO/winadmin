using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Tests;

public sealed class RoleStorageTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private readonly SqliteWinAdminDbContext _db;

    public RoleStorageTests()
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
    public async Task Deleting_role_removes_its_permissions_and_assignments()
    {
        var role = new RoleEntity { Name = "Кадры" };
        role.Permissions.Add(new RolePermissionEntity { PermissionId = "ou.users.read", ScopeJson = "[\"OU=HR\"]" });
        role.Assignments.Add(new RoleAssignmentEntity { PrincipalType = "LocalUser", PrincipalId = "u1", DisplayName = "u1" });
        _db.Roles.Add(role);
        await _db.SaveChangesAsync();

        _db.Roles.Remove(role);
        await _db.SaveChangesAsync();

        Assert.Equal(0, await _db.RolePermissions.CountAsync());
        Assert.Equal(0, await _db.RoleAssignments.CountAsync());
    }

    [Fact]
    public async Task Same_principal_cannot_get_same_role_twice()
    {
        var role = new RoleEntity { Name = "R" };
        _db.Roles.Add(role);
        _db.RoleAssignments.Add(new RoleAssignmentEntity { RoleId = role.Id, PrincipalType = "ApiKey", PrincipalId = "k", DisplayName = "k" });
        _db.RoleAssignments.Add(new RoleAssignmentEntity { RoleId = role.Id, PrincipalType = "ApiKey", PrincipalId = "k", DisplayName = "k" });
        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
    }

    [Fact]
    public async Task Module_state_round_trips()
    {
        _db.ModuleStates.Add(new ModuleStateEntity { Id = "services", Enabled = false, SettingsJson = "{}" });
        await _db.SaveChangesAsync();
        Assert.False((await _db.ModuleStates.SingleAsync()).Enabled);
    }
}
