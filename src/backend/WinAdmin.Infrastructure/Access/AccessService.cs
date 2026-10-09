using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Infrastructure.Access;

public static class RoleMapping
{
    public static RoleSnapshot ToSnapshot(RoleEntity role) => new(
        role.Id,
        role.Id == BuiltInRoles.AdministratorId,
        role.Permissions.Select(p => new RoleGrant(p.PermissionId, ScopeJson.Parse(p.ScopeJson))).ToList());
}

/// <summary>Права субъекта и его групп AD; кэш 60 с по субъекту и набору групп.</summary>
public sealed class AccessService(IServiceScopeFactory scopes, PermissionCatalog catalog, TimeProvider? time = null) : IAccessService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, EffectivePermissions Value)> _cache = new();

    public async Task<EffectivePermissions> GetAsync(PrincipalRef principal, CancellationToken ct = default)
    {
        string key = principal.Key + "|" + string.Join(",", principal.GroupSids.Order(StringComparer.OrdinalIgnoreCase));
        var now = _time.GetUtcNow();
        if (_cache.TryGetValue(key, out var hit) && now - hit.At < Ttl)
            return hit.Value;

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WinAdminDbContext>();
        string type = principal.Type.ToString();
        string groupType = PrincipalType.AdGroup.ToString();
        var groups = principal.GroupSids.ToList();

        var roleIds = await db.RoleAssignments.AsNoTracking()
            .Where(a => (a.PrincipalType == type && a.PrincipalId == principal.Id)
                        || (a.PrincipalType == groupType && groups.Contains(a.PrincipalId)))
            .Select(a => a.RoleId)
            .Distinct()
            .ToListAsync(ct);
        var roles = await db.Roles.AsNoTracking()
            .Include(r => r.Permissions)
            .Where(r => roleIds.Contains(r.Id))
            .ToListAsync(ct);

        var result = PermissionEvaluator.Evaluate(roles.Select(RoleMapping.ToSnapshot), catalog);
        _cache[key] = (now, result);
        return result;
    }

    public void Invalidate() => _cache.Clear();
}
