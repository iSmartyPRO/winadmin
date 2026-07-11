using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;

namespace WinAdmin.Api.Controllers;

/// <summary>Базовый контроллер: префикс маршрута, актор и запись аудита.</summary>
[ApiController]
[Authorize]
[Route("api/v1/[controller]")]
[Produces("application/json")]
public abstract class WinAdminControllerBase : ControllerBase
{
    protected string Actor => User.Identity?.Name ?? "unknown";
    protected string? SourceIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    /// <summary>Записывает управляющее действие в аудит и возвращает результат как HTTP-ответ.</summary>
    protected async Task<IActionResult> AuditedAsync(IAuditService audit, string action, string? target, OperationResult result)
    {
        await audit.WriteAsync(new AuditEntryDto
        {
            Actor = Actor,
            Action = action,
            Target = target,
            Success = result.Success,
            Details = result.Message,
            SourceIp = SourceIp,
        });
        return result.Success ? Ok(result) : StatusCode(StatusCodes.Status409Conflict, result);
    }
}
