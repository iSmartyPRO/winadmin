using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WinAdmin.Api.Auth;
using WinAdmin.Core.Abstractions;

namespace WinAdmin.Api.Controllers;

/// <summary>Кто я и что мне можно: права с областями и включённые модули.</summary>
[Authorize]
[Route("api/v1/me")]
public sealed class MeController(AccessContextFactory contexts, IModuleRegistry modules) : WinAdminControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var access = await contexts.CreateAsync(User, ct);
        if (access is null) return Unauthorized();
        return Ok(new
        {
            actor = access.Actor,
            principal = access.Principal.Key,
            permissions = access.Permissions.Map.ToDictionary(kv => kv.Key, kv => kv.Value.Items),
            modules = modules.Modules
                .Where(m => modules.GetState(m.Id).Enabled)
                .Select(m => new { id = m.Id, title = m.Title }),
        });
    }
}
