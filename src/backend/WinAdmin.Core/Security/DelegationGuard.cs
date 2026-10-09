using WinAdmin.Core.Modules;

namespace WinAdmin.Core.Security;

/// <summary>Отказ в делегировании: перечень прав/областей, которые действующее лицо не может выдать.</summary>
public sealed class AccessDeniedException(string message, IReadOnlyList<string> violations) : Exception(message)
{
    public IReadOnlyList<string> Violations { get; } = violations;
}

/// <summary>Нельзя выдать больше, чем есть у себя: каждое право — своё, область — не шире своей.</summary>
public static class DelegationGuard
{
    public static IEnumerable<RoleGrant> AdministratorGrants(PermissionCatalog catalog)
        => catalog.All.Select(p => new RoleGrant(p.Id, null));

    public static IReadOnlyList<string> Violations(EffectivePermissions actor, IEnumerable<RoleGrant> grants, PermissionCatalog catalog)
    {
        var violations = new List<string>();
        foreach (var grant in grants)
        {
            var have = actor.ScopeFor(grant.PermissionId);
            if (have is null)
            {
                violations.Add($"нет права «{grant.PermissionId}»");
                continue;
            }
            if (have.IsUnrestricted)
                continue;

            var def = catalog.Find(grant.PermissionId);
            if (grant.Scope is null || def is null || !def.Scopable)
            {
                violations.Add($"«{grant.PermissionId}»: можно выдать только в пределах своей области");
                continue;
            }

            var own = new ScopeDefinition(have.Items!);
            var provider = catalog.ModuleOf(grant.PermissionId)?.Scope;
            bool subset = provider?.IsSubsetOf(grant.Scope, own)
                          ?? grant.Scope.Items.All(i => own.Items.Contains(i, StringComparer.OrdinalIgnoreCase));
            if (!subset)
                violations.Add($"«{grant.PermissionId}»: область шире вашей");
        }
        return violations;
    }
}
