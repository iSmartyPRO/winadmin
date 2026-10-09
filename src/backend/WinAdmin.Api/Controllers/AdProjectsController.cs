using Microsoft.AspNetCore.Mvc;
using WinAdmin.Api.Auth;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.ActiveDirectory.Users;
using WinAdmin.Core.Security;

namespace WinAdmin.Api.Controllers;

/// <summary>Проекты (OU первого уровня) в пределах области; общий для модулей AD (3c добавит ad-folders.read).</summary>
[PlatformErrors]
[AdErrors]
[Route("/api/v1/ad/projects")]
public sealed class AdProjectsController(IAdReader reader, IModuleRegistry modules, AccessContextFactory contexts) : WinAdminControllerBase
{
    private static readonly (string Module, string Permission)[] Sources =
    [
        (AdUsersModule.ModuleId, PermissionIds.AdUsersRead),
    ];

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var enabled = Sources.Where(s => modules.GetState(s.Module).Enabled).ToList();
        if (enabled.Count == 0) return NotFound();
        var actor = await contexts.CreateAsync(User, ct);
        if (actor is null) return Unauthorized();
        var allowed = enabled.Where(s => actor.Permissions.Has(s.Permission)).ToList();
        if (allowed.Count == 0) return StatusCode(StatusCodes.Status403Forbidden);

        var projects = await reader.ListProjectsAsync(false, ct);
        var visible = allowed.SelectMany(s => AdGuard.InScopeProjects(actor, s.Permission, projects))
            .DistinctBy(p => p.Dn, StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(p => new { dn = p.Dn, name = p.Name });
        return Ok(visible);
    }
}
