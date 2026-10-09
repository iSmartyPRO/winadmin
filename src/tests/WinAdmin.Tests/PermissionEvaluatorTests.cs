using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;

namespace WinAdmin.Tests;

/// <summary>Модуль с областью (как будущий AD) для тестов прав.</summary>
internal sealed class ScopedTestModule : IWinAdminModule
{
    public const string Read = "ou.users.read";
    public const string Edit = "ou.users.edit";
    public string Id => "ou";
    public string Title => "OU";
    public string? Description => null;
    public ModuleRequirements Requirements => ModuleRequirements.None;
    public bool EnabledByDefault => true;
    public IReadOnlyList<PermissionDefinition> Permissions { get; } =
        [new(Read, "Чтение", Scopable: true), new(Edit, "Правка", Scopable: true)];
    public Type? SettingsType => null;
    public IScopeProvider? Scope { get; } = new SuffixScope();
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration) { }

    /// <summary>Как DN: «OU=A,OU=B» входит в «OU=B».</summary>
    private sealed class SuffixScope : IScopeProvider
    {
        public string Title => "OU";
        public ScopeDefinition Normalize(ScopeDefinition scope) => scope;
        public bool IsSubsetOf(ScopeDefinition candidate, ScopeDefinition container)
            => candidate.Items.All(c => container.Items.Any(k =>
                c.Equals(k, StringComparison.OrdinalIgnoreCase) || c.EndsWith("," + k, StringComparison.OrdinalIgnoreCase)));
    }
}

public sealed class PermissionEvaluatorTests
{
    private static readonly PermissionCatalog Catalog = new([.. BuiltInModules.All, new ScopedTestModule()]);
    private static RoleSnapshot Role(params RoleGrant[] grants) => new(Guid.NewGuid().ToString(), false, grants);
    private static ScopeDefinition S(params string[] items) => new(items);

    [Fact]
    public void Unions_scopes_of_roles_granting_same_permission()
    {
        var e = PermissionEvaluator.Evaluate(
            [Role(new RoleGrant(ScopedTestModule.Read, S("OU=X"))), Role(new RoleGrant(ScopedTestModule.Read, S("OU=Y", "ou=x")))], Catalog);

        var scope = e.ScopeFor(ScopedTestModule.Read)!;
        Assert.False(scope.IsUnrestricted);
        Assert.Equal(2, scope.Items!.Count);
    }

    [Fact]
    public void Unrestricted_grant_wins()
    {
        var e = PermissionEvaluator.Evaluate(
            [Role(new RoleGrant(ScopedTestModule.Read, S("OU=X"))), Role(new RoleGrant(ScopedTestModule.Read, null))], Catalog);
        Assert.True(e.ScopeFor(ScopedTestModule.Read)!.IsUnrestricted);
    }

    [Fact]
    public void Scope_on_non_scopable_permission_is_ignored()
    {
        var e = PermissionEvaluator.Evaluate([Role(new RoleGrant(PermissionIds.ServicesRead, S("x")))], Catalog);
        Assert.True(e.ScopeFor(PermissionIds.ServicesRead)!.IsUnrestricted);
    }

    [Fact]
    public void Unknown_permissions_are_ignored()
    {
        var e = PermissionEvaluator.Evaluate([Role(new RoleGrant("gone.module.read", null))], Catalog);
        Assert.False(e.Has("gone.module.read"));
        Assert.Empty(e.Map);
    }

    [Fact]
    public void Administrator_gets_every_permission_unrestricted()
    {
        var e = PermissionEvaluator.Evaluate([new RoleSnapshot(BuiltInRoles.AdministratorId, true, [])], Catalog);
        Assert.All(Catalog.All, p => Assert.True(e.ScopeFor(p.Id)!.IsUnrestricted));
    }

    [Fact]
    public void Principal_claims_round_trip_and_fall_back_to_name_identifier()
    {
        var withClaim = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(PrincipalClaims.Type, PrincipalClaims.Format(PrincipalType.AdUser, "S-1-5-21-1")),
            new Claim(PrincipalClaims.GroupType, "S-1-5-21-512"),
        ], "Bearer"));
        var p = PrincipalClaims.Parse(withClaim, "ApiKey")!;
        Assert.Equal(PrincipalType.AdUser, p.Type);
        Assert.Equal("S-1-5-21-1", p.Id);
        Assert.Equal(["S-1-5-21-512"], p.GroupSids);

        // JWT, выпущенный до обновления: только NameIdentifier.
        var legacy = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u1")], "AuthenticationTypes.Federation"));
        Assert.Equal(new PrincipalRef(PrincipalType.LocalUser, "u1", []).Key, PrincipalClaims.Parse(legacy, "ApiKey")!.Key);

        var key = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "k1")], "ApiKey"));
        Assert.Equal(PrincipalType.ApiKey, PrincipalClaims.Parse(key, "ApiKey")!.Type);

        Assert.Null(PrincipalClaims.Parse(new ClaimsPrincipal(new ClaimsIdentity()), "ApiKey"));
    }
}
