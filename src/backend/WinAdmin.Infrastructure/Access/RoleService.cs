using Microsoft.EntityFrameworkCore;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Infrastructure.Access;

public sealed class RoleService(WinAdminDbContext db, PermissionCatalog catalog, IAccessService access, IAuditService audit) : IRoleService
{
    public async Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken ct = default)
    {
        var roles = await db.Roles.AsNoTracking().Include(r => r.Permissions).OrderBy(r => r.Name).ToListAsync(ct);
        var counts = await db.RoleAssignments.AsNoTracking().GroupBy(a => a.RoleId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return roles
            .OrderByDescending(r => r.IsBuiltin).ThenBy(r => r.Name)
            .Select(r => ToDto(r, counts.GetValueOrDefault(r.Id))).ToList();
    }

    public async Task<RoleDto> CreateAsync(SaveRoleRequest request, IAccessContext actor, CancellationToken ct = default)
    {
        var (name, grants) = Validate(request);
        Demand(actor, grants);
        await EnsureUniqueNameAsync(name, null, ct);

        var role = new RoleEntity { Name = name, Description = request.Description?.Trim() };
        foreach (var g in grants)
            role.Permissions.Add(new RolePermissionEntity { PermissionId = g.PermissionId, ScopeJson = ScopeJson.Serialize(g.Scope) });
        db.Roles.Add(role);
        await db.SaveChangesAsync(ct);
        await AuditAsync(actor, "role.create", role.Name, ct);
        access.Invalidate();
        return ToDto(role, 0);
    }

    public async Task<RoleDto> UpdateAsync(string id, SaveRoleRequest request, IAccessContext actor, CancellationToken ct = default)
    {
        var role = await LoadRoleAsync(id, ct);
        if (role.IsBuiltin)
            throw new InvalidOperationException("Встроенную роль нельзя изменить.");
        var (name, grants) = Validate(request);
        Demand(actor, GrantsOf(role));
        Demand(actor, grants);
        await EnsureUniqueNameAsync(name, role.Id, ct);

        role.Name = name;
        role.Description = request.Description?.Trim();
        db.RolePermissions.RemoveRange(role.Permissions);
        role.Permissions.Clear();
        foreach (var g in grants)
            role.Permissions.Add(new RolePermissionEntity { RoleId = role.Id, PermissionId = g.PermissionId, ScopeJson = ScopeJson.Serialize(g.Scope) });
        await db.SaveChangesAsync(ct);
        await AuditAsync(actor, "role.update", role.Name, ct);
        access.Invalidate();
        return ToDto(role, await db.RoleAssignments.CountAsync(a => a.RoleId == role.Id, ct));
    }

    public async Task DeleteAsync(string id, IAccessContext actor, CancellationToken ct = default)
    {
        var role = await LoadRoleAsync(id, ct);
        if (role.IsBuiltin)
            throw new InvalidOperationException("Встроенную роль нельзя удалить.");
        Demand(actor, GrantsOf(role));
        db.Roles.Remove(role);
        await db.SaveChangesAsync(ct);
        await AuditAsync(actor, "role.delete", role.Name, ct);
        access.Invalidate();
    }

    public async Task<IReadOnlyList<RoleAssignmentDto>> ListAssignmentsAsync(
        PrincipalType? type = null, string? principalId = null, string? roleId = null, CancellationToken ct = default)
    {
        var query = db.RoleAssignments.AsNoTracking().Include(a => a.Role).AsQueryable();
        if (type is not null) query = query.Where(a => a.PrincipalType == type.ToString());
        if (principalId is not null) query = query.Where(a => a.PrincipalId == principalId);
        if (roleId is not null) query = query.Where(a => a.RoleId == roleId);
        var list = await query.ToListAsync(ct);
        return list.OrderBy(a => a.Role.Name).ThenBy(a => a.DisplayName).Select(ToDto).ToList();
    }

    public async Task<RoleAssignmentDto> AssignAsync(CreateAssignmentRequest request, IAccessContext actor, CancellationToken ct = default)
    {
        var role = await LoadRoleAsync(request.RoleId, ct);
        Demand(actor, GrantsOf(role));
        string displayName = await ResolvePrincipalAsync(request, ct);
        string type = request.PrincipalType.ToString();
        if (await db.RoleAssignments.AnyAsync(a => a.RoleId == role.Id && a.PrincipalType == type && a.PrincipalId == request.PrincipalId, ct))
            throw new InvalidOperationException($"Роль «{role.Name}» уже назначена «{displayName}».");

        var entity = new RoleAssignmentEntity
        {
            RoleId = role.Id, Role = role, PrincipalType = type, PrincipalId = request.PrincipalId,
            DisplayName = displayName, CreatedBy = actor.Actor,
        };
        db.RoleAssignments.Add(entity);
        await db.SaveChangesAsync(ct);
        await AuditAsync(actor, "role.assign", $"{role.Name} → {displayName}", ct);
        access.Invalidate();
        return ToDto(entity);
    }

    public async Task UnassignAsync(string assignmentId, IAccessContext actor, CancellationToken ct = default)
    {
        var entity = await db.RoleAssignments.Include(a => a.Role).ThenInclude(r => r.Permissions)
            .FirstOrDefaultAsync(a => a.Id == assignmentId, ct)
            ?? throw new KeyNotFoundException("Назначение не найдено.");
        Demand(actor, GrantsOf(entity.Role));
        if (entity.RoleId == BuiltInRoles.AdministratorId && await CountActiveAdministratorsAsync(entity.Id, ct) == 0)
            throw new InvalidOperationException("Нельзя снять последнее назначение роли «Администратор».");

        db.RoleAssignments.Remove(entity);
        await db.SaveChangesAsync(ct);
        await AuditAsync(actor, "role.unassign", $"{entity.Role.Name} → {entity.DisplayName}", ct);
        access.Invalidate();
    }

    public async Task EnsureNotLastAdministratorAsync(PrincipalType type, string principalId, CancellationToken ct = default)
    {
        string t = type.ToString();
        var own = await db.RoleAssignments.AsNoTracking()
            .Where(a => a.RoleId == BuiltInRoles.AdministratorId && a.PrincipalType == t && a.PrincipalId == principalId)
            .Select(a => a.Id).ToListAsync(ct);
        if (own.Count == 0) return;
        if (await CountActiveAdministratorsAsync(null, ct, excludePrincipal: (t, principalId)) == 0)
            throw new InvalidOperationException("Это последний активный администратор WinAdmin — сначала назначьте роль «Администратор» другому.");
    }

    public async Task RemovePrincipalAsync(PrincipalType type, string principalId, CancellationToken ct = default)
    {
        string t = type.ToString();
        var rows = await db.RoleAssignments.Where(a => a.PrincipalType == t && a.PrincipalId == principalId).ToListAsync(ct);
        db.RoleAssignments.RemoveRange(rows);
        await db.SaveChangesAsync(ct);
        access.Invalidate();
    }

    // ── helpers ──────────────────────────────────────────────────

    private (string Name, List<RoleGrant> Grants) Validate(SaveRoleRequest request)
    {
        string name = request.Name?.Trim() ?? "";
        if (name.Length is 0 or > 100)
            throw new ArgumentException("Название роли: от 1 до 100 символов.");

        var grants = new Dictionary<string, RoleGrant>(StringComparer.Ordinal);
        foreach (var p in request.Permissions ?? [])
        {
            var def = catalog.Find(p.PermissionId)
                      ?? throw new ArgumentException($"Неизвестное право «{p.PermissionId}».");
            ScopeDefinition? scope = null;
            if (p.Scope is not null)
            {
                if (!def.Scopable)
                    throw new ArgumentException($"Право «{def.Id}» не поддерживает область.");
                var items = p.Scope.Select(i => i?.Trim() ?? "").Where(i => i.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (items.Count == 0)
                    throw new ArgumentException($"Область права «{def.Id}» пуста (уберите область, чтобы дать право без ограничений).");
                scope = new ScopeDefinition(items);
                var provider = catalog.ModuleOf(def.Id)?.Scope;
                if (provider is not null)
                    scope = provider.Normalize(scope);
            }
            grants[def.Id] = new RoleGrant(def.Id, scope);
        }
        return (name, grants.Values.ToList());
    }

    private void Demand(IAccessContext actor, IEnumerable<RoleGrant> grants)
    {
        var violations = DelegationGuard.Violations(actor.Permissions, grants, catalog);
        if (violations.Count > 0)
            throw new AccessDeniedException("Недостаточно прав для этой роли.", violations);
    }

    private IEnumerable<RoleGrant> GrantsOf(RoleEntity role)
        => role.Id == BuiltInRoles.AdministratorId
            ? DelegationGuard.AdministratorGrants(catalog)
            : RoleMapping.ToSnapshot(role).Grants;

    private async Task<RoleEntity> LoadRoleAsync(string id, CancellationToken ct)
        => await db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == id, ct)
           ?? throw new KeyNotFoundException("Роль не найдена.");

    private async Task EnsureUniqueNameAsync(string name, string? exceptId, CancellationToken ct)
    {
        // Сравнение в памяти: LOWER() в SQLite не понижает кириллицу, а ролей немного.
        var names = await db.Roles.AsNoTracking().Where(r => r.Id != exceptId).Select(r => r.Name).ToListAsync(ct);
        if (names.Any(n => string.Equals(n, name, StringComparison.CurrentCultureIgnoreCase)))
            throw new InvalidOperationException($"Роль «{name}» уже существует.");
    }

    private async Task<string> ResolvePrincipalAsync(CreateAssignmentRequest request, CancellationToken ct)
    {
        switch (request.PrincipalType)
        {
            case PrincipalType.LocalUser:
                var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == request.PrincipalId, ct)
                           ?? throw new ArgumentException("Пользователь не найден.");
                return user.Login;
            case PrincipalType.ApiKey:
                var key = await db.ApiKeys.AsNoTracking().FirstOrDefaultAsync(k => k.Id == request.PrincipalId, ct)
                          ?? throw new ArgumentException("API-ключ не найден.");
                return key.Name;
            default:
                if (!request.PrincipalId.StartsWith("S-1-", StringComparison.Ordinal))
                    throw new ArgumentException("Для учётной записи AD нужен SID (S-1-…).");
                return string.IsNullOrWhiteSpace(request.DisplayName) ? request.PrincipalId : request.DisplayName.Trim();
        }
    }

    /// <summary>Активные назначения «Администратор»: пользователь включён, ключ не отозван и не истёк, AD — всегда.</summary>
    private async Task<int> CountActiveAdministratorsAsync(string? excludeAssignmentId, CancellationToken ct,
        (string Type, string Id)? excludePrincipal = null)
    {
        var rows = await db.RoleAssignments.AsNoTracking()
            .Where(a => a.RoleId == BuiltInRoles.AdministratorId && a.Id != excludeAssignmentId).ToListAsync(ct);
        int active = 0;
        var now = DateTimeOffset.UtcNow;
        foreach (var a in rows)
        {
            if (excludePrincipal is { } ex && a.PrincipalType == ex.Type && a.PrincipalId == ex.Id) continue;
            bool isActive = a.PrincipalType switch
            {
                nameof(PrincipalType.LocalUser) => await db.Users.AnyAsync(u => u.Id == a.PrincipalId && u.IsActive, ct),
                nameof(PrincipalType.ApiKey) => (await db.ApiKeys.AsNoTracking().FirstOrDefaultAsync(k => k.Id == a.PrincipalId, ct))
                    is { IsRevoked: false } k && (k.ExpiresAt is null || k.ExpiresAt > now),
                _ => true,
            };
            if (isActive) active++;
        }
        return active;
    }

    private Task AuditAsync(IAccessContext actor, string action, string target, CancellationToken ct)
        => audit.WriteAsync(new AuditEntryDto { Actor = actor.Actor, Action = action, Target = target, Success = true }, ct);

    private RoleDto ToDto(RoleEntity r, int assignments)
    {
        var grants = r.Id == BuiltInRoles.AdministratorId
            ? catalog.All.Select(p => new RoleGrantDto(p.Id, null)).ToList()
            : r.Permissions.OrderBy(p => p.PermissionId)
                .Select(p => new RoleGrantDto(p.PermissionId, ScopeJson.Parse(p.ScopeJson)?.Items)).ToList();
        return new RoleDto(r.Id, r.Name, r.Description, r.IsBuiltin, grants, assignments);
    }

    private static RoleAssignmentDto ToDto(RoleAssignmentEntity a) => new(
        a.Id, a.RoleId, a.Role.Name, Enum.Parse<PrincipalType>(a.PrincipalType), a.PrincipalId, a.DisplayName, a.CreatedAt);
}
