using WinAdmin.Core.Models;
using WinAdmin.Core.Security;

namespace WinAdmin.Core.Abstractions;

/// <summary>
/// Роли и назначения. Ошибки: ArgumentException — неверные данные; AccessDeniedException —
/// нарушение делегирования; InvalidOperationException — конфликт; KeyNotFoundException — нет объекта.
/// </summary>
public interface IRoleService
{
    Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken ct = default);
    Task<RoleDto> CreateAsync(SaveRoleRequest request, IAccessContext actor, CancellationToken ct = default);
    Task<RoleDto> UpdateAsync(string id, SaveRoleRequest request, IAccessContext actor, CancellationToken ct = default);
    Task DeleteAsync(string id, IAccessContext actor, CancellationToken ct = default);

    Task<IReadOnlyList<RoleAssignmentDto>> ListAssignmentsAsync(
        PrincipalType? type = null, string? principalId = null, string? roleId = null, CancellationToken ct = default);
    Task<RoleAssignmentDto> AssignAsync(CreateAssignmentRequest request, IAccessContext actor, CancellationToken ct = default);
    Task UnassignAsync(string assignmentId, IAccessContext actor, CancellationToken ct = default);

    /// <summary>409, если субъект — последний активный администратор.</summary>
    Task EnsureNotLastAdministratorAsync(PrincipalType type, string principalId, CancellationToken ct = default);

    /// <summary>
    /// Управлять субъектом (пароль, отключение, удаление, отзыв) можно, только если все его роли
    /// в пределах прав действующего лица — иначе AccessDeniedException (иначе сброс пароля
    /// администратора был бы путём к его правам).
    /// </summary>
    Task DemandControlOverAsync(PrincipalType type, string principalId, IAccessContext actor, CancellationToken ct = default);

    /// <summary>Сколько активных субъектов с ролью «Администратор» (для предупреждения при старте).</summary>
    Task<int> CountActiveAdministratorsAsync(CancellationToken ct = default);

    /// <summary>Удаляет все назначения субъекта (при удалении пользователя/ключа).</summary>
    Task RemovePrincipalAsync(PrincipalType type, string principalId, CancellationToken ct = default);
}
