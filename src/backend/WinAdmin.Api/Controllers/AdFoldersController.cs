using Microsoft.AspNetCore.Mvc;
using WinAdmin.Api.Auth;
using WinAdmin.Api.Modules;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory.Folders;
using WinAdmin.Core.Security;

namespace WinAdmin.Api.Controllers;

public sealed record PathRequest(string Path);

[WinAdminModule(AdFoldersModule.ModuleId)]
[PlatformErrors]
[AdErrors]
[Route("/api/v1/ad/folders")]
public sealed class AdFoldersController(IAdFoldersService folders, AccessContextFactory contexts) : WinAdminControllerBase
{
    private async Task<IAccessContext> ActorAsync(CancellationToken ct)
        => await contexts.CreateAsync(User, ct) ?? throw new AccessDeniedException("Не удалось определить пользователя.", []);

    [HttpGet]
    [RequirePermission(PermissionIds.AdFoldersRead)]
    public async Task<FolderCatalogResult> List([FromQuery] string? project, [FromQuery] string? q, CancellationToken ct)
        => await folders.ListAsync(await ActorAsync(ct), string.IsNullOrWhiteSpace(project) ? null : project, q, ct);

    [HttpGet("user/{sam}")]
    [RequirePermission(PermissionIds.AdFoldersRead)]
    public async Task<IActionResult> UserAccess(string sam, CancellationToken ct) => Ok(await folders.UserAccessAsync(await ActorAsync(ct), sam, ct));

    [HttpPost("membership")]
    [RequirePermission(PermissionIds.AdFoldersMembership)]
    public async Task<IActionResult> Membership([FromBody] MembershipRequest request, CancellationToken ct)
        => Ok(await folders.ChangeMembershipAsync(await ActorAsync(ct), request, ct));

    [HttpPost("preview")]
    [RequirePermission(PermissionIds.AdFoldersCreate)]
    public async Task<FolderPreview> Preview([FromBody] CreateFolderRequest request, CancellationToken ct)
        => await folders.PreviewAsync(await ActorAsync(ct), request, ct);

    [HttpPost]
    [RequirePermission(PermissionIds.AdFoldersCreate)]
    public async Task<IActionResult> Create([FromBody] CreateFolderRequest request, CancellationToken ct)
        => Ok(await folders.CreateFolderAsync(await ActorAsync(ct), request, ct));

    [HttpGet("acl")]
    [RequirePermission(PermissionIds.AdFoldersRead)]
    public async Task<FolderAclState> Acl([FromQuery] string path, CancellationToken ct) => await folders.InspectAclAsync(await ActorAsync(ct), path, ct);

    [HttpPost("acl-fix")]
    [RequirePermission(PermissionIds.AdFoldersCreate)]
    public async Task<IActionResult> FixAcl([FromBody] PathRequest request, CancellationToken ct)
        => Ok(await folders.FixAclAsync(await ActorAsync(ct), request.Path, ct));
}
