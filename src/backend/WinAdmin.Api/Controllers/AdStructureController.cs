using Microsoft.AspNetCore.Mvc;
using WinAdmin.Api.Auth;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.Models;
using WinAdmin.Core.Security;

namespace WinAdmin.Api.Controllers;

public sealed record SaveAdStructureRequest(
    string? RootOu, string? UsersOuName, List<string>? HiddenOus, AdWriteMode WriteMode, string? WriteLogin, string? WritePassword);

[PlatformErrors]
[RequirePermission(PermissionIds.PlatformDirectoryManage)]
[Route("/api/v1/settings/ad")]
public sealed class AdStructureController(IAdStructureStore store, IAuditService audit) : WinAdminControllerBase
{
    [HttpGet]
    public Task<AdStructureSettings> Get(CancellationToken ct) => store.GetAsync(ct);

    [HttpPut]
    public async Task<AdStructureSettings> Put([FromBody] SaveAdStructureRequest request, CancellationToken ct)
    {
        var settings = new AdStructureSettings(request.RootOu, request.UsersOuName ?? "Users", request.HiddenOus ?? [],
            request.WriteMode, request.WriteLogin);
        await store.SaveAsync(settings, request.WritePassword, ct);
        var saved = await store.GetAsync(ct);
        await audit.WriteAsync(new AuditEntryDto
        {
            Actor = Actor, Action = "settings.ad", Success = true, SourceIp = SourceIp,
            Details = $"root={saved.RootOu}, users={saved.UsersOuName}, hidden=[{string.Join(", ", saved.HiddenOus)}], " +
                      $"mode={saved.WriteMode}, login={saved.WriteLogin}" + (request.WritePassword is null ? "" : ", пароль изменён"),
        }, ct);
        return saved;
    }
}
