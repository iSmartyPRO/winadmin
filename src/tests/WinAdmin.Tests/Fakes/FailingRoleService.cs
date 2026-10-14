using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Security;

namespace WinAdmin.Tests.Fakes;

/// <summary>Настоящий сервис ролей, у которого назначение номер <c>failAt()</c> (с 1) падает.</summary>
public sealed class FailingRoleService(IRoleService inner, Func<int?> failAt) : IRoleService
{
    private int _assigns;

    public Task<RoleDto> CreateAsync(SaveRoleRequest request, IAccessContext actor, CancellationToken ct = default) => inner.CreateAsync(request, actor, ct);
    public Task<RoleDto> UpdateAsync(string id, SaveRoleRequest request, IAccessContext actor, CancellationToken ct = default) => inner.UpdateAsync(id, request, actor, ct);
    public Task DeleteAsync(string id, IAccessContext actor, CancellationToken ct = default) => inner.DeleteAsync(id, actor, ct);
    public Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken ct = default) => inner.ListAsync(ct);
    public Task<IReadOnlyList<RoleAssignmentDto>> ListAssignmentsAsync(
        PrincipalType? type = null, string? principalId = null, string? roleId = null, CancellationToken ct = default)
        => inner.ListAssignmentsAsync(type, principalId, roleId, ct);

    public Task<RoleAssignmentDto> AssignAsync(CreateAssignmentRequest request, IAccessContext actor, CancellationToken ct = default)
        => ++_assigns == failAt() ? throw new InvalidOperationException("сбой записи назначения") : inner.AssignAsync(request, actor, ct);

    public Task UnassignAsync(string assignmentId, IAccessContext actor, CancellationToken ct = default) => inner.UnassignAsync(assignmentId, actor, ct);
    public Task EnsureNotLastAdministratorAsync(PrincipalType type, string principalId, CancellationToken ct = default) => inner.EnsureNotLastAdministratorAsync(type, principalId, ct);
    public Task DemandControlOverAsync(PrincipalType type, string principalId, IAccessContext actor, CancellationToken ct = default) => inner.DemandControlOverAsync(type, principalId, actor, ct);
    public Task<int> CountActiveAdministratorsAsync(CancellationToken ct = default) => inner.CountActiveAdministratorsAsync(ct);
    public Task RemovePrincipalAsync(PrincipalType type, string principalId, CancellationToken ct = default) => inner.RemovePrincipalAsync(type, principalId, ct);
}
