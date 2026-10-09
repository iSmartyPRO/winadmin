using Microsoft.AspNetCore.Mvc;
using WinAdmin.Api.Auth;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.EnvironmentChecks;
using WinAdmin.Core.Security;

namespace WinAdmin.Api.Controllers;

[RequirePermission(PermissionIds.PlatformEnvironmentCheck)]
[Route("/api/v1/environment")]
public sealed class EnvironmentController(IEnvironmentService environment) : WinAdminControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<EnvironmentReport>> Run([FromQuery] string? module, [FromQuery] string? depth, CancellationToken ct)
        => environment.RunAsync(string.IsNullOrWhiteSpace(module) ? null : module,
            string.Equals(depth, "full", StringComparison.OrdinalIgnoreCase) ? CheckDepth.Full : CheckDepth.Quick, ct);

    [HttpGet("latest")]
    public IReadOnlyList<EnvironmentReport> Latest() => environment.Latest;
}
