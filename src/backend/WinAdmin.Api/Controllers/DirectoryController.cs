using Microsoft.AspNetCore.Mvc;
using WinAdmin.Api.Auth;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.Models;
using WinAdmin.Core.Security;

namespace WinAdmin.Api.Controllers;

[PlatformErrors]
public sealed class DirectoryController(IDirectorySettingsStore settings, IDirectoryService directory, IAuditService audit)
    : WinAdminControllerBase
{
    [HttpGet("/api/v1/settings/directory")]
    [RequirePermission(PermissionIds.PlatformDirectoryManage)]
    public async Task<DirectorySettings> Get(CancellationToken ct) => await settings.GetAsync(ct);

    [HttpPut("/api/v1/settings/directory")]
    [RequirePermission(PermissionIds.PlatformDirectoryManage)]
    public async Task<DirectorySettings> Put([FromBody] DirectorySettings request, CancellationToken ct)
    {
        await settings.SaveAsync(request, ct);
        var saved = await settings.GetAsync(ct);
        await audit.WriteAsync(new AuditEntryDto
        {
            Actor = Actor, Action = "settings.directory", Success = true, SourceIp = SourceIp,
            Details = $"enabled={saved.Enabled}, domain={saved.Domain}, server={saved.Server ?? "auto"}, ldaps={saved.UseLdaps}",
        }, ct);
        return saved;
    }

    [HttpPost("/api/v1/settings/directory/test")]
    [RequirePermission(PermissionIds.PlatformDirectoryManage)]
    public Task<IReadOnlyList<DirectoryTestStep>> Test(CancellationToken ct) => directory.TestConnectionAsync(ct);

    [HttpGet("/api/v1/directory/search")]
    [RequirePermission(PermissionIds.PlatformRolesManage)]
    public async Task<IActionResult> Search([FromQuery] string q, [FromQuery] string? kind, CancellationToken ct)
    {
        DirectoryObjectKind? k = kind?.ToLowerInvariant() switch
        {
            "user" => DirectoryObjectKind.User,
            "group" => DirectoryObjectKind.Group,
            _ => null,
        };
        try
        {
            var found = await directory.SearchAsync(q ?? "", k, 25, ct);
            return Ok(found.Select(o => new
            {
                sid = o.Sid,
                kind = o.Kind == DirectoryObjectKind.Group ? "group" : "user",
                samAccountName = o.SamAccountName,
                displayName = o.DisplayName,
                upn = o.Upn,
                enabled = o.Enabled,
            }));
        }
        catch (DirectoryUnavailableException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = ex.Message });
        }
    }
}
