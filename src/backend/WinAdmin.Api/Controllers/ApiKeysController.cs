using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Security;
using WinAdmin.Api.Auth;

namespace WinAdmin.Api.Controllers;

/// <summary>Управление API-ключами (требует scope admin).</summary>
[RequirePermission(PermissionIds.PlatformApiKeysManage)]
[Route("api/v1/apikeys")]
public sealed class ApiKeysController : WinAdminControllerBase
{
    private readonly IApiKeyService _keys;
    private readonly IAuditService _audit;

    public ApiKeysController(IApiKeyService keys, IAuditService audit)
    {
        _keys = keys;
        _audit = audit;
    }

    /// <summary>Список ключей (без секретов).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ApiKeyDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ApiKeyDto>>> List(CancellationToken ct)
        => Ok(await _keys.ListAsync(ct));

    /// <summary>Создать ключ. Секрет возвращается один раз.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(CreatedApiKey), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateApiKeyRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(OperationResult.Fail("Укажите имя ключа"));

        var created = await _keys.CreateAsync(request, ct);
        await _audit.WriteAsync(new AuditEntryDto
        {
            Actor = Actor, Action = "apikey.create", Target = created.Key.Id,
            Success = true, Details = null, SourceIp = SourceIp,
        }, ct);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    /// <summary>Отозвать ключ.</summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(typeof(OperationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Revoke(string id, CancellationToken ct)
    {
        bool ok = await _keys.RevokeAsync(id, ct);
        await _audit.WriteAsync(new AuditEntryDto
        {
            Actor = Actor, Action = "apikey.revoke", Target = id, Success = ok, SourceIp = SourceIp,
        }, ct);
        return ok ? Ok(OperationResult.Ok("Ключ отозван")) : NotFound();
    }
}
