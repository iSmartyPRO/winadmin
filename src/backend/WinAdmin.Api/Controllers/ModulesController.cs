using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using WinAdmin.Api.Auth;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;

namespace WinAdmin.Api.Controllers;

/// <summary>Модули: состояние, включение, настройки.</summary>
[RequirePermission(PermissionIds.PlatformModulesManage)]
[PlatformErrors]
[Route("api/v1/modules")]
public sealed class ModulesController(IModuleRegistry modules) : WinAdminControllerBase
{
    public sealed record UpdateModuleRequest(bool? Enabled, JsonObject? Settings);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = new List<object>();
        foreach (var m in modules.Modules)
        {
            var state = modules.GetState(m.Id);
            result.Add(new
            {
                id = m.Id, title = m.Title, description = m.Description,
                enabled = state.Enabled, available = state.Available, unavailableReason = state.UnavailableReason,
                scopable = m.Scope is not null, scopeTitle = m.Scope?.Title,
                permissions = m.Permissions.Select(PermissionView),
                settingsSchema = modules.GetSettingsSchema(m.Id),
                settings = await modules.GetSettingsViewAsync(m.Id, ct),
            });
        }
        return Ok(result);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateModuleRequest request, CancellationToken ct)
    {
        if (request.Settings is not null)
            await modules.SaveSettingsAsync(id, request.Settings, Actor, ct);
        if (request.Enabled is bool enabled)
            await modules.SetEnabledAsync(id, enabled, Actor, ct);
        return NoContent();
    }

    internal static object PermissionView(PermissionDefinition p)
        => new { id = p.Id, title = p.Title, description = p.Description, scopable = p.Scopable, dangerous = p.Dangerous };
}
