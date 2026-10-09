using Microsoft.AspNetCore.Mvc;
using WinAdmin.Api.Auth;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Security;

namespace WinAdmin.Api.Controllers;

[RequirePermission(PermissionIds.PlatformRolesManage)]
[PlatformErrors]
[Route("api/v1/role-assignments")]
public sealed class RoleAssignmentsController(IRoleService roles, AccessContextFactory contexts) : WinAdminControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PrincipalType? principalType, [FromQuery] string? principalId,
        [FromQuery] string? roleId, CancellationToken ct)
        => Ok(await roles.ListAssignmentsAsync(principalType, principalId, roleId, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAssignmentRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await roles.AssignAsync(request, await ActorAsync(ct), ct));

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        await roles.UnassignAsync(id, await ActorAsync(ct), ct);
        return NoContent();
    }

    private async Task<IAccessContext> ActorAsync(CancellationToken ct)
        => await contexts.CreateAsync(User, ct) ?? throw new AccessDeniedException("Не удалось определить пользователя.", []);
}
