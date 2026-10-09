using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure.Access;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Tests;

public sealed class RoleServiceTests : IDisposable
{
    private static readonly PermissionCatalog Catalog = new([.. BuiltInModules.All, new ScopedTestModule()]);
    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private readonly SqliteWinAdminDbContext _db;
    private readonly Mock<IAccessService> _access = new();
    private readonly RoleService _roles;

    private static IAccessContext Admin => new AccessContext(new PrincipalRef(PrincipalType.LocalUser, "admin", []), "admin",
        PermissionEvaluator.Evaluate([new RoleSnapshot(BuiltInRoles.AdministratorId, true, [])], Catalog));

    private static IAccessContext Delegate(params RoleGrant[] grants) => new AccessContext(
        new PrincipalRef(PrincipalType.LocalUser, "hrlead", []), "hrlead",
        PermissionEvaluator.Evaluate([new RoleSnapshot("d", false, [new(PermissionIds.PlatformRolesManage, null), .. grants])], Catalog));

    public RoleServiceTests()
    {
        _connection.Open();
        _db = new SqliteWinAdminDbContext(new DbContextOptionsBuilder<SqliteWinAdminDbContext>().UseSqlite(_connection).Options);
        _db.Database.Migrate();
        _db.Roles.Add(new RoleEntity { Id = BuiltInRoles.AdministratorId, Name = BuiltInRoles.AdministratorName, IsBuiltin = true });
        _db.Users.Add(new UserEntity { Id = "u-admin", Login = "admin", PasswordHash = "x" });
        _db.Users.Add(new UserEntity { Id = "u-second", Login = "second", PasswordHash = "x" });
        _db.SaveChanges();
        _roles = new RoleService(_db, Catalog, _access.Object, Mock.Of<IAuditService>());
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private Task<RoleAssignmentDto> AssignAdmin(string userId)
        => _roles.AssignAsync(new CreateAssignmentRequest(BuiltInRoles.AdministratorId, PrincipalType.LocalUser, userId, null), Admin);

    [Fact]
    public async Task Creates_role_and_invalidates_cache()
    {
        var role = await _roles.CreateAsync(new SaveRoleRequest(" Кадры ", "HR",
            [new(ScopedTestModule.Read, [" OU=HR ", "OU=HR"]), new(PermissionIds.ServicesRead, null)]), Admin);

        Assert.Equal("Кадры", role.Name);
        Assert.Equal(["OU=HR"], role.Permissions.Single(p => p.PermissionId == ScopedTestModule.Read).Scope);
        _access.Verify(a => a.Invalidate(), Times.AtLeastOnce);
    }

    [Theory]
    [InlineData("", PermissionIds.ServicesRead)]
    [InlineData("R", "no.such.permission")]
    public async Task Rejects_invalid_roles(string name, string permission)
        => await Assert.ThrowsAsync<ArgumentException>(() =>
            _roles.CreateAsync(new SaveRoleRequest(name, null, [new(permission, null)]), Admin));

    [Fact]
    public async Task Rejects_duplicate_name_case_insensitive()
    {
        await _roles.CreateAsync(new SaveRoleRequest("Кадры", null, [new(PermissionIds.ServicesRead, null)]), Admin);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _roles.CreateAsync(new SaveRoleRequest("кадры", null, [new(PermissionIds.ServicesRead, null)]), Admin));
    }

    [Fact]
    public async Task Delegate_cannot_create_role_beyond_own_rights_and_nothing_changes()
    {
        var hr = Delegate(new RoleGrant(ScopedTestModule.Read, new(["OU=HR"])));
        var ex = await Assert.ThrowsAsync<AccessDeniedException>(() => _roles.CreateAsync(
            new SaveRoleRequest("Шире", null, [new(ScopedTestModule.Read, ["OU=Corp"]), new(PermissionIds.PowerManage, null)]), hr));

        Assert.Equal(2, ex.Violations.Count);
        Assert.Equal(1, await _db.Roles.CountAsync()); // только «Администратор»
    }

    [Fact]
    public async Task Delegate_cannot_assign_administrator()
    {
        var hr = Delegate(new RoleGrant(PermissionIds.ServicesRead, null));
        await Assert.ThrowsAsync<AccessDeniedException>(() => _roles.AssignAsync(
            new CreateAssignmentRequest(BuiltInRoles.AdministratorId, PrincipalType.LocalUser, "u-second", null), hr));
    }

    [Fact]
    public async Task Builtin_role_is_immutable()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _roles.UpdateAsync(BuiltInRoles.AdministratorId, new SaveRoleRequest("X", null, []), Admin));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _roles.DeleteAsync(BuiltInRoles.AdministratorId, Admin));
    }

    [Fact]
    public async Task Assignment_requires_existing_principal_and_is_unique()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => AssignAdmin("nobody"));
        var first = await AssignAdmin("u-admin");
        Assert.Equal("admin", first.DisplayName);
        await Assert.ThrowsAsync<InvalidOperationException>(() => AssignAdmin("u-admin"));
    }

    [Fact]
    public async Task Last_active_administrator_cannot_be_removed()
    {
        var a = await AssignAdmin("u-admin");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _roles.UnassignAsync(a.Id, Admin));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _roles.EnsureNotLastAdministratorAsync(PrincipalType.LocalUser, "u-admin"));

        // Второй администратор отключён — не считается.
        var b = await AssignAdmin("u-second");
        (await _db.Users.FindAsync("u-second"))!.IsActive = false;
        await _db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => _roles.UnassignAsync(a.Id, Admin));

        // Активный второй — можно.
        (await _db.Users.FindAsync("u-second"))!.IsActive = true;
        await _db.SaveChangesAsync();
        await _roles.UnassignAsync(a.Id, Admin);
        Assert.Single(await _roles.ListAssignmentsAsync());
        _ = b;
    }

    [Fact]
    public async Task Expired_api_key_is_not_an_active_administrator()
    {
        _db.ApiKeys.Add(new ApiKeyEntity { Id = "k-old", Name = "old", KeyHash = "h", ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1) });
        await _db.SaveChangesAsync();
        var user = await AssignAdmin("u-admin");
        await _roles.AssignAsync(new CreateAssignmentRequest(BuiltInRoles.AdministratorId, PrincipalType.ApiKey, "k-old", null), Admin);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _roles.UnassignAsync(user.Id, Admin));
    }

    [Fact]
    public async Task Ad_assignment_does_not_count_as_active_administrator_yet()
    {
        var local = await AssignAdmin("u-admin");
        await _roles.AssignAsync(new CreateAssignmentRequest(BuiltInRoles.AdministratorId, PrincipalType.AdGroup, "S-1-5-21-1-2-3-512", "Domain Admins"), Admin);

        // Без входа через AD эта группа не может управлять WinAdmin — последнего локального снять нельзя.
        await Assert.ThrowsAsync<InvalidOperationException>(() => _roles.UnassignAsync(local.Id, Admin));
        Assert.Equal(1, await _roles.CountActiveAdministratorsAsync());
    }

    [Fact]
    public async Task Removing_principal_drops_its_assignments()
    {
        await AssignAdmin("u-admin");
        var role = await _roles.CreateAsync(new SaveRoleRequest("R", null, [new(PermissionIds.ServicesRead, null)]), Admin);
        await _roles.AssignAsync(new CreateAssignmentRequest(role.Id, PrincipalType.LocalUser, "u-second", null), Admin);

        await _roles.RemovePrincipalAsync(PrincipalType.LocalUser, "u-second");

        Assert.DoesNotContain(await _roles.ListAssignmentsAsync(), x => x.PrincipalId == "u-second");
    }
}
