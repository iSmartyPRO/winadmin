using Microsoft.AspNetCore.Mvc;
using WinAdmin.Api.Auth;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;

namespace WinAdmin.Api.Controllers;

/// <summary>Каталог прав для редактора ролей: ядро и модули (в т.ч. выключенные — роль можно подготовить заранее).</summary>
[RequirePermission(PermissionIds.PlatformRolesManage)]
[Route("api/v1/permissions")]
public sealed class PermissionsController(PermissionCatalog catalog) : WinAdminControllerBase
{
    [HttpGet]
    public IActionResult List()
    {
        var groups = new List<object>
        {
            new { id = "platform", title = "WinAdmin", scopable = false, scopeTitle = (string?)null,
                  permissions = PermissionIds.Platform.Select(ModulesController.PermissionView) },
        };
        groups.AddRange(catalog.Modules.Select(m => (object)new
        {
            id = m.Id, title = m.Title, scopable = m.Scope is not null, scopeTitle = m.Scope?.Title,
            permissions = m.Permissions.Select(ModulesController.PermissionView),
        }));
        return Ok(groups);
    }
}
