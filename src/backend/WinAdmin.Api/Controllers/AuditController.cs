using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Security;

namespace WinAdmin.Api.Controllers;

/// <summary>Журнал аудита управляющих действий (требует scope admin).</summary>
[Authorize(Policy = "scope:" + Scopes.Admin)]
public sealed class AuditController : WinAdminControllerBase
{
    private readonly IAuditService _audit;

    public AuditController(IAuditService audit) => _audit = audit;

    /// <summary>Последние записи аудита (по умолчанию 200).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AuditEntryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AuditEntryDto>>> Get(
        [FromQuery] int limit = 200, [FromQuery] string? actor = null, CancellationToken ct = default)
        => Ok(await _audit.QueryAsync(limit, actor, ct));
}
