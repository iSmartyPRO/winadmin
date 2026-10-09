using Microsoft.AspNetCore.Mvc;
using WinAdmin.Api.Auth;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Security;

namespace WinAdmin.Api.Controllers;

[RequirePermission(PermissionIds.PlatformRolesManage)]
[PlatformErrors]
[Route("api/v1/roles")]
public sealed class RolesController(IRoleService roles, AccessContextFactory contexts) : WinAdminControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await roles.ListAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveRoleRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await roles.CreateAsync(request, await ActorAsync(ct), ct));

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] SaveRoleRequest request, CancellationToken ct)
        => Ok(await roles.UpdateAsync(id, request, await ActorAsync(ct), ct));

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        await roles.DeleteAsync(id, await ActorAsync(ct), ct);
        return NoContent();
    }

    private async Task<IAccessContext> ActorAsync(CancellationToken ct)
        => await contexts.CreateAsync(User, ct) ?? throw new AccessDeniedException("Не удалось определить пользователя.", []);
}
