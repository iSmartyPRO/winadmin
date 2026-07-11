using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Security;

namespace WinAdmin.Api.Controllers;

/// <summary>Windows-службы: список и управление.</summary>
public sealed class ServicesController : WinAdminControllerBase
{
    private readonly IServiceControlService _services;
    private readonly IAuditService _audit;

    public ServicesController(IServiceControlService services, IAuditService audit)
    {
        _services = services;
        _audit = audit;
    }

    /// <summary>Список служб с состоянием и типом запуска.</summary>
    [HttpGet]
    [Authorize(Policy = "scope:" + Scopes.ServicesRead)]
    [ProducesResponseType(typeof(IReadOnlyList<ServiceInfo>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<ServiceInfo>> Get() => Ok(_services.GetServices());

    /// <summary>Управление службой: start, stop или restart.</summary>
    [HttpPost("{name}/{action}")]
    [Authorize(Policy = "scope:" + Scopes.ServicesManage)]
    [ProducesResponseType(typeof(OperationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(OperationResult), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Control(string name, string action)
    {
        if (!Enum.TryParse<ServiceAction>(action, ignoreCase: true, out var parsed))
            return BadRequest(OperationResult.Fail($"Неизвестное действие '{action}'. Допустимо: start, stop, restart"));

        var result = _services.Control(name, parsed);
        return await AuditedAsync(_audit, $"service.{parsed.ToString().ToLowerInvariant()}", name, result);
    }
}
