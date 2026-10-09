using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using WinAdmin.Api.Auth;
using WinAdmin.Api.Modules;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.ActiveDirectory.Users;
using WinAdmin.Core.Security;

namespace WinAdmin.Api.Controllers;

public sealed record AttributesRequest(Dictionary<string, string?> Attributes);
public sealed record ProjectRequest(string ProjectDn);
public sealed record PasswordRequest(string? Password, bool Generate, bool MustChange = true);

/// <summary>AD: недоступен — 503, AD отклонил — 422 с текстом.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class AdErrorsAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        context.Result = context.Exception switch
        {
            DirectoryUnavailableException e => new ObjectResult(new { message = e.Message }) { StatusCode = 503 },
            AdWriteException e => new ObjectResult(new { message = e.Message }) { StatusCode = 422 },
            _ => null,
        };
        if (context.Result is not null) context.ExceptionHandled = true;
    }
}

[WinAdminModule(AdUsersModule.ModuleId)]
[PlatformErrors]
[AdErrors]
[Route("/api/v1/ad/users")]
public sealed class AdUsersController(IAdUsersService users, AccessContextFactory contexts) : WinAdminControllerBase
{
    private async Task<IAccessContext> ActorAsync(CancellationToken ct)
        => await contexts.CreateAsync(User, ct) ?? throw new AccessDeniedException("Не удалось определить пользователя.", []);

    [HttpGet]
    [RequirePermission(PermissionIds.AdUsersRead)]
    public async Task<IReadOnlyList<AdUserView>> List([FromQuery] string? project, [FromQuery] string? status, [FromQuery] string? q, CancellationToken ct)
        => await users.ListAsync(await ActorAsync(ct), string.IsNullOrWhiteSpace(project) ? null : project,
            Enum.TryParse<AdUserStatus>(status, true, out var s) ? s : AdUserStatus.All, q, ct);

    [HttpGet("{sam}")]
    [RequirePermission(PermissionIds.AdUsersRead)]
    public async Task<AdUserCard> Get(string sam, CancellationToken ct) => await users.GetAsync(await ActorAsync(ct), sam, ct);

    [HttpGet("{sam}/photo")]
    [RequirePermission(PermissionIds.AdUsersRead)]
    public async Task<IActionResult> Photo(string sam, CancellationToken ct)
    {
        var photo = await users.GetPhotoAsync(await ActorAsync(ct), sam, ct);
        if (photo is null) return NotFound();
        return File(photo, photo.Length > 1 && photo[0] == 0x89 ? "image/png" : "image/jpeg");
    }

    [HttpGet("{sam}/history")]
    [RequirePermission(PermissionIds.AdUsersRead)]
    public async Task<IActionResult> History(string sam, CancellationToken ct) => Ok(await users.HistoryAsync(await ActorAsync(ct), sam, ct));

    [HttpPut("{sam}/attributes")]
    [RequirePermission(PermissionIds.AdUsersEdit)]
    public async Task<AdUserView> Attributes(string sam, [FromBody] AttributesRequest request, CancellationToken ct)
        => await users.UpdateAttributesAsync(await ActorAsync(ct), sam, request.Attributes ?? [], ct);

    [HttpPut("{sam}/photo")]
    [RequirePermission(PermissionIds.AdUsersEdit)]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<IActionResult> SetPhoto(string sam, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms, ct);
        await users.SetPhotoAsync(await ActorAsync(ct), sam, ms.ToArray(), ct);
        return NoContent();
    }

    [HttpDelete("{sam}/photo")]
    [RequirePermission(PermissionIds.AdUsersEdit)]
    public async Task<IActionResult> RemovePhoto(string sam, CancellationToken ct)
    {
        await users.SetPhotoAsync(await ActorAsync(ct), sam, null, ct);
        return NoContent();
    }

    [HttpPost("{sam}/move")]
    [RequirePermission(PermissionIds.AdUsersMove)]
    public async Task<AdUserView> Move(string sam, [FromBody] ProjectRequest request, CancellationToken ct)
        => await users.MoveAsync(await ActorAsync(ct), sam, request.ProjectDn, ct);

    [HttpPost("{sam}/password")]
    [RequirePermission(PermissionIds.AdUsersPassword)]
    public async Task<IActionResult> Password(string sam, [FromBody] PasswordRequest request, CancellationToken ct)
        => Ok(new { password = await users.ResetPasswordAsync(await ActorAsync(ct), sam, request.Password, request.Generate, request.MustChange, ct) });

    [HttpPost("{sam}/deactivate")]
    [RequirePermission(PermissionIds.AdUsersOffboard)]
    public async Task<IActionResult> Deactivate(string sam, CancellationToken ct) => Ok(await users.DeactivateAsync(await ActorAsync(ct), sam, ct));

    [HttpPost("{sam}/activate")]
    [RequirePermission(PermissionIds.AdUsersOffboard)]
    public async Task<IActionResult> Activate(string sam, [FromBody] ProjectRequest request, CancellationToken ct)
        => Ok(await users.ActivateAsync(await ActorAsync(ct), sam, request.ProjectDn, ct));
}
