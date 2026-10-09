using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Security;
using WinAdmin.Api.Auth;

namespace WinAdmin.Api.Controllers;

[RequirePermission(PermissionIds.PlatformApiKeysManage)]
[PlatformErrors]
[Route("api/v1/apikeys")]
public sealed class ApiKeysController(IApiKeyService keys, IAuditService audit, IRoleService roles, AccessContextFactory contexts)
    : WinAdminControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ApiKeyDto>>> List(CancellationToken ct)
    {
        var assignments = await roles.ListAssignmentsAsync(PrincipalType.ApiKey, ct: ct);
        var list = await keys.ListAsync(ct);
        return Ok(list.Select(k => k with
        {
            Roles = assignments.Where(a => a.PrincipalId == k.Id).Select(a => a.RoleName).ToList(),
        }).ToList());
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateApiKeyRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(OperationResult.Fail("Укажите имя ключа"));
        if (request.RoleIds.Count == 0)
            return BadRequest(OperationResult.Fail("Выберите хотя бы одну роль"));
        var actor = await contexts.CreateAsync(User, ct) ?? throw new AccessDeniedException("Не удалось определить пользователя.", []);
        if (!actor.Permissions.Has(PermissionIds.PlatformRolesManage))
            return StatusCode(StatusCodes.Status403Forbidden, OperationResult.Fail("Выдавать роли ключам может только тот, у кого есть право управления ролями."));

        var created = await keys.CreateAsync(request, ct);
        var granted = new List<string>();
        try
        {
            foreach (var roleId in request.RoleIds.Distinct())
                granted.Add((await roles.AssignAsync(new CreateAssignmentRequest(roleId, PrincipalType.ApiKey, created.Key.Id, null), actor, ct)).RoleName);
        }
        catch
        {
            await roles.RemovePrincipalAsync(PrincipalType.ApiKey, created.Key.Id, ct);
            await keys.RevokeAsync(created.Key.Id, ct);
            throw;
        }

        await audit.WriteAsync(new AuditEntryDto
        {
            Actor = Actor, Action = "apikey.create", Target = created.Key.Id, Success = true,
            Details = $"роли: {string.Join(", ", granted)}", SourceIp = SourceIp,
        }, ct);
        return StatusCode(StatusCodes.Status201Created, created with { Key = created.Key with { Roles = granted } });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Revoke(string id, CancellationToken ct)
    {
        var actor = await contexts.CreateAsync(User, ct) ?? throw new AccessDeniedException("Не удалось определить пользователя.", []);
        await roles.DemandControlOverAsync(PrincipalType.ApiKey, id, actor, ct);
        await roles.EnsureNotLastAdministratorAsync(PrincipalType.ApiKey, id, ct);
        bool ok = await keys.RevokeAsync(id, ct);
        await audit.WriteAsync(new AuditEntryDto { Actor = Actor, Action = "apikey.revoke", Target = id, Success = ok, SourceIp = SourceIp }, ct);
        return ok ? Ok(OperationResult.Ok("Ключ отозван")) : NotFound();
    }
}
