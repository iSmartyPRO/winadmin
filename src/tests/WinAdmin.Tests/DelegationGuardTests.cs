using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;

namespace WinAdmin.Tests;

public sealed class DelegationGuardTests
{
    private static readonly PermissionCatalog Catalog = new([.. BuiltInModules.All, new ScopedTestModule()]);

    private static EffectivePermissions Actor(params RoleGrant[] grants)
        => PermissionEvaluator.Evaluate([new RoleSnapshot("r", false, grants)], Catalog);

    [Fact]
    public void Can_grant_subset_of_own_permissions_and_scope()
    {
        var actor = Actor(new RoleGrant(PermissionIds.ServicesRead, null), new RoleGrant(ScopedTestModule.Read, new(["OU=Corp"])));
        var violations = DelegationGuard.Violations(actor,
            [new RoleGrant(PermissionIds.ServicesRead, null), new RoleGrant(ScopedTestModule.Read, new(["OU=HR,OU=Corp"]))], Catalog);
        Assert.Empty(violations);
    }

    [Fact]
    public void Cannot_grant_permission_actor_lacks()
    {
        var actor = Actor(new RoleGrant(PermissionIds.ServicesRead, null));
        var violations = DelegationGuard.Violations(actor, [new RoleGrant(PermissionIds.ServicesManage, null)], Catalog);
        Assert.Single(violations);
        Assert.Contains(PermissionIds.ServicesManage, violations[0]);
    }

    [Fact]
    public void Cannot_widen_scope_or_drop_it()
    {
        var actor = Actor(new RoleGrant(ScopedTestModule.Read, new(["OU=HR,OU=Corp"])));
        Assert.NotEmpty(DelegationGuard.Violations(actor, [new RoleGrant(ScopedTestModule.Read, new(["OU=Corp"]))], Catalog));
        Assert.NotEmpty(DelegationGuard.Violations(actor, [new RoleGrant(ScopedTestModule.Read, null)], Catalog));
    }

    [Fact]
    public void Only_full_administrator_can_hand_out_administrator()
    {
        var partial = Actor(new RoleGrant(PermissionIds.PlatformRolesManage, null), new RoleGrant(PermissionIds.ServicesRead, null));
        Assert.NotEmpty(DelegationGuard.Violations(partial, DelegationGuard.AdministratorGrants(Catalog), Catalog));

        var admin = PermissionEvaluator.Evaluate([new RoleSnapshot(BuiltInRoles.AdministratorId, true, [])], Catalog);
        Assert.Empty(DelegationGuard.Violations(admin, DelegationGuard.AdministratorGrants(Catalog), Catalog));
    }
}
