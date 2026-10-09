using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Security;
using WinAdmin.Api.Auth;

namespace WinAdmin.Api.Controllers;

[RequirePermission(PermissionIds.PlatformUsersManage)]
[PlatformErrors]
[Route("api/v1/users")]
public sealed class UsersController(IUserService users, IRoleService roles, AccessContextFactory contexts) : WinAdminControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> List(CancellationToken ct)
    {
        var assignments = await roles.ListAssignmentsAsync(PrincipalType.LocalUser, ct: ct);
        var list = await users.ListAsync(ct);
        return Ok(list.Select(u => u with
        {
            Roles = assignments.Where(a => a.PrincipalId == u.Id).Select(a => a.RoleName).ToList(),
        }).ToList());
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Login) || string.IsNullOrEmpty(request.Password))
            return BadRequest(new { message = "Укажите логин и пароль." });
        var actor = await contexts.CreateAsync(User, ct) ?? throw new AccessDeniedException("Не удалось определить пользователя.", []);

        UserDto dto;
        try
        {
            dto = await users.CreateAsync(request, ct);
        }
        catch (Exception ex) when (ex.ToString().Contains("UNIQUE") || ex.ToString().Contains("unique"))
        {
            return Conflict(new { message = $"Пользователь «{request.Login}» уже существует" });
        }

        var granted = new List<string>();
        try
        {
            foreach (var roleId in request.RoleIds.Distinct())
                granted.Add((await roles.AssignAsync(new CreateAssignmentRequest(roleId, PrincipalType.LocalUser, dto.Id, null), actor, ct)).RoleName);
        }
        catch
        {
            // Роли выдать нельзя — пользователь без ролей не нужен.
            await roles.RemovePrincipalAsync(PrincipalType.LocalUser, dto.Id, ct);
            await users.DeleteAsync(dto.Id, ct);
            throw;
        }
        return StatusCode(StatusCodes.Status201Created, dto with { Roles = granted });
    }

    [HttpPut("{id}/password")]
    public async Task<IActionResult> ChangePassword(string id, [FromBody] ChangePasswordRequest request, CancellationToken ct)
        => await users.ChangePasswordAsync(id, request.NewPassword, ct) ? NoContent() : NotFound();

    [HttpPut("{id}/active")]
    public async Task<IActionResult> SetActive(string id, [FromBody] SetActiveRequest request, CancellationToken ct)
    {
        if (!request.IsActive)
            await roles.EnsureNotLastAdministratorAsync(PrincipalType.LocalUser, id, ct);
        return await users.SetActiveAsync(id, request.IsActive, ct) ? NoContent() : NotFound();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        await roles.EnsureNotLastAdministratorAsync(PrincipalType.LocalUser, id, ct);
        if (!await users.DeleteAsync(id, ct)) return NotFound();
        await roles.RemovePrincipalAsync(PrincipalType.LocalUser, id, ct);
        return NoContent();
    }
}
