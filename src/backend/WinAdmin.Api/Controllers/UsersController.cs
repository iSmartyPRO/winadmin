using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Security;
using WinAdmin.Api.Auth;

namespace WinAdmin.Api.Controllers;

/// <summary>Управление пользователями (требует scope admin).</summary>
[RequirePermission(PermissionIds.PlatformUsersManage)]
[Route("api/v1/users")]
public sealed class UsersController : WinAdminControllerBase
{
    private readonly IUserService _users;

    public UsersController(IUserService users) => _users = users;

    /// <summary>Список пользователей.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<UserDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> List(CancellationToken ct)
        => Ok(await _users.ListAsync(ct));

    /// <summary>Создать пользователя.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request, CancellationToken ct)
    {
        try
        {
            var dto = await _users.CreateAsync(request, ct);
            return CreatedAtAction(nameof(List), new { }, dto);
        }
        catch (Exception ex) when (ex.Message.Contains("UNIQUE") || ex.Message.Contains("unique"))
        {
            return Conflict(new { message = $"Пользователь '{request.Login}' уже существует" });
        }
    }

    /// <summary>Сменить пароль.</summary>
    [HttpPut("{id}/password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangePassword(string id, [FromBody] ChangePasswordRequest request, CancellationToken ct)
        => await _users.ChangePasswordAsync(id, request.NewPassword, ct) ? NoContent() : NotFound();

    /// <summary>Активировать / деактивировать.</summary>
    [HttpPut("{id}/active")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetActive(string id, [FromBody] SetActiveRequest request, CancellationToken ct)
        => await _users.SetActiveAsync(id, request.IsActive, ct) ? NoContent() : NotFound();

    /// <summary>Удалить пользователя.</summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
        => await _users.DeleteAsync(id, ct) ? NoContent() : NotFound();
}
