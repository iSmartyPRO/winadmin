using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure.Access;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Tests;

public sealed class AccessServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private readonly ServiceProvider _sp;
    private readonly ManualTimeProvider _time = new(DateTimeOffset.UtcNow);
    private readonly AccessService _access;
    private static readonly PermissionCatalog Catalog = new([.. BuiltInModules.All, new ScopedTestModule()]);

    public AccessServiceTests()
    {
        _connection.Open();
        _sp = new ServiceCollection()
            .AddDbContext<WinAdminDbContext, SqliteWinAdminDbContext>(o => o.UseSqlite(_connection))
            .BuildServiceProvider();
        using (var scope = _sp.CreateScope())
            scope.ServiceProvider.GetRequiredService<WinAdminDbContext>().Database.Migrate();
        _access = new AccessService(_sp.GetRequiredService<IServiceScopeFactory>(), Catalog, _time);
    }

    public void Dispose()
    {
        _sp.Dispose();
        _connection.Dispose();
    }

    private void Seed(Action<WinAdminDbContext> seed)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WinAdminDbContext>();
        seed(db);
        db.SaveChanges();
    }

    private static RoleEntity Role(string name, string permission, string? scopeJson = null)
    {
        var r = new RoleEntity { Name = name };
        r.Permissions.Add(new RolePermissionEntity { PermissionId = permission, ScopeJson = scopeJson });
        return r;
    }

    [Fact]
    public async Task Combines_direct_and_group_assignments()
    {
        var direct = Role("D", PermissionIds.ServicesRead);
        var viaGroup = Role("G", ScopedTestModule.Read, "[\"OU=HR\"]");
        Seed(db =>
        {
            db.Roles.AddRange(direct, viaGroup);
            db.RoleAssignments.Add(new RoleAssignmentEntity { RoleId = direct.Id, PrincipalType = "AdUser", PrincipalId = "S-1-5-21-7", DisplayName = "u" });
            db.RoleAssignments.Add(new RoleAssignmentEntity { RoleId = viaGroup.Id, PrincipalType = "AdGroup", PrincipalId = "S-1-5-21-900", DisplayName = "g" });
        });

        var p = await _access.GetAsync(new PrincipalRef(PrincipalType.AdUser, "S-1-5-21-7", ["S-1-5-21-900"]));

        Assert.True(p.Has(PermissionIds.ServicesRead));
        Assert.Equal(["OU=HR"], p.ScopeFor(ScopedTestModule.Read)!.Items);
        Assert.False(p.Has(PermissionIds.ServicesManage));
    }

    [Fact]
    public async Task Administrator_assignment_grants_everything()
    {
        Seed(db =>
        {
            db.Roles.Add(new RoleEntity { Id = BuiltInRoles.AdministratorId, Name = BuiltInRoles.AdministratorName, IsBuiltin = true });
            db.RoleAssignments.Add(new RoleAssignmentEntity { RoleId = BuiltInRoles.AdministratorId, PrincipalType = "LocalUser", PrincipalId = "u1", DisplayName = "u1" });
        });

        var p = await _access.GetAsync(new PrincipalRef(PrincipalType.LocalUser, "u1", []));
        Assert.All(Catalog.All, d => Assert.True(p.Has(d.Id)));
    }

    [Fact]
    public async Task Caches_for_60_seconds_and_invalidates_on_demand()
    {
        var role = Role("R", PermissionIds.ServicesRead);
        Seed(db => db.Roles.Add(role));
        var who = new PrincipalRef(PrincipalType.ApiKey, "k1", []);
        Assert.False((await _access.GetAsync(who)).Has(PermissionIds.ServicesRead));

        Seed(db => db.RoleAssignments.Add(new RoleAssignmentEntity { RoleId = role.Id, PrincipalType = "ApiKey", PrincipalId = "k1", DisplayName = "k" }));
        Assert.False((await _access.GetAsync(who)).Has(PermissionIds.ServicesRead)); // из кэша

        _time.Advance(TimeSpan.FromSeconds(61));
        Assert.True((await _access.GetAsync(who)).Has(PermissionIds.ServicesRead));

        Seed(db => db.RoleAssignments.RemoveRange(db.RoleAssignments));
        _access.Invalidate();
        Assert.False((await _access.GetAsync(who)).Has(PermissionIds.ServicesRead));
    }
}
