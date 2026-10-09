using WinAdmin.Core.Modules;

namespace WinAdmin.Core.Security;

public enum PrincipalType { LocalUser, AdUser, AdGroup, ApiKey }

/// <summary>Субъект доступа; GroupSids — группы AD (транзитивно), для остальных — пусто.</summary>
public sealed record PrincipalRef(PrincipalType Type, string Id, IReadOnlyList<string> GroupSids)
{
    public string Key => $"{Type}:{Id}";
}

/// <summary>Область итогового права: Items == null — без ограничений.</summary>
public sealed class EffectiveScope
{
    private EffectiveScope(IReadOnlyList<string>? items) => Items = items;

    public static EffectiveScope Unrestricted { get; } = new(null);

    public static EffectiveScope Of(IEnumerable<string> items)
        => new(items.Distinct(StringComparer.OrdinalIgnoreCase).ToList());

    public IReadOnlyList<string>? Items { get; }
    public bool IsUnrestricted => Items is null;
}

public sealed record RoleGrant(string PermissionId, ScopeDefinition? Scope);

public sealed record RoleSnapshot(string RoleId, bool IsAdministrator, IReadOnlyList<RoleGrant> Grants);

/// <summary>Итоговые права субъекта: право → область.</summary>
public sealed class EffectivePermissions(IReadOnlyDictionary<string, EffectiveScope> map)
{
    public static EffectivePermissions None { get; } = new(new Dictionary<string, EffectiveScope>());

    public IReadOnlyDictionary<string, EffectiveScope> Map { get; } = map;
    public bool Has(string permissionId) => Map.ContainsKey(permissionId);
    public EffectiveScope? ScopeFor(string permissionId) => Map.GetValueOrDefault(permissionId);
}

public static class PermissionEvaluator
{
    /// <summary>
    /// Объединение ролей: право без области (или необластное право) в любой роли — без ограничений;
    /// иначе — объединение областей всех ролей, дающих это право. Неизвестные права игнорируются.
    /// </summary>
    public static EffectivePermissions Evaluate(IEnumerable<RoleSnapshot> roles, PermissionCatalog catalog)
    {
        var unrestricted = new HashSet<string>(StringComparer.Ordinal);
        var scoped = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var role in roles)
        {
            var grants = role.IsAdministrator ? DelegationGuard.AdministratorGrants(catalog) : role.Grants;
            foreach (var grant in grants)
            {
                var def = catalog.Find(grant.PermissionId);
                if (def is null) continue;
                if (grant.Scope is null || !def.Scopable)
                {
                    unrestricted.Add(def.Id);
                    continue;
                }
                if (!scoped.TryGetValue(def.Id, out var set))
                    scoped[def.Id] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                set.UnionWith(grant.Scope.Items);
            }
        }

        var map = new Dictionary<string, EffectiveScope>(StringComparer.Ordinal);
        foreach (var id in unrestricted)
            map[id] = EffectiveScope.Unrestricted;
        foreach (var (id, set) in scoped)
            map.TryAdd(id, EffectiveScope.Of(set));
        return new EffectivePermissions(map);
    }
}

/// <summary>Кто действует и что ему можно (для сервисов и контроллеров).</summary>
public interface IAccessContext
{
    PrincipalRef Principal { get; }
    string Actor { get; }
    EffectivePermissions Permissions { get; }
}

public sealed record AccessContext(PrincipalRef Principal, string Actor, EffectivePermissions Permissions) : IAccessContext;
