using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Security;

namespace WinAdmin.Api.Controllers;

/// <summary>
/// Настройки: чёрный список учётных записей, которые скрываются из журналов
/// событий (требует scope admin).
/// </summary>
[Authorize(Policy = "scope:" + Scopes.Admin)]
[Route("api/v1/settings/excluded-users")]
public sealed class ExcludedUsersController : WinAdminControllerBase
{
    private readonly IExcludedUserService _excluded;

    public ExcludedUsersController(IExcludedUserService excluded) => _excluded = excluded;

    /// <summary>Список исключённых учётных записей.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ExcludedUserDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ExcludedUserDto>>> List(CancellationToken ct)
        => Ok(await _excluded.ListAsync(ct));

    /// <summary>Добавить учётную запись в чёрный список.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ExcludedUserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Add([FromBody] AddExcludedUserRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.UserName))
            return BadRequest(new { message = "Укажите имя учётной записи." });

        try
        {
            var dto = await _excluded.AddAsync(request.UserName, ct);
            return CreatedAtAction(nameof(List), new { }, dto);
        }
        catch (Exception ex) when (ex.Message.Contains("UNIQUE") || ex.Message.Contains("unique"))
        {
            return Conflict(new { message = $"«{request.UserName.Trim()}» уже в списке исключений." });
        }
    }

    /// <summary>Удалить учётную запись из чёрного списка.</summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Remove(string id, CancellationToken ct)
        => await _excluded.RemoveAsync(id, ct) ? NoContent() : NotFound();
}
