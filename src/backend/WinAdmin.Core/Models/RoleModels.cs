using WinAdmin.Core.Security;

namespace WinAdmin.Core.Models;

public sealed record RoleGrantDto(string PermissionId, IReadOnlyList<string>? Scope);

public sealed record RoleDto(
    string Id, string Name, string? Description, bool IsBuiltin,
    IReadOnlyList<RoleGrantDto> Permissions, int AssignmentCount);

public sealed record SaveRoleRequest(string Name, string? Description, List<RoleGrantDto> Permissions);

public sealed record RoleAssignmentDto(
    string Id, string RoleId, string RoleName, PrincipalType PrincipalType,
    string PrincipalId, string DisplayName, DateTimeOffset CreatedAt);

public sealed record CreateAssignmentRequest(string RoleId, PrincipalType PrincipalType, string PrincipalId, string? DisplayName);
