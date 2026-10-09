using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Security;
using WinAdmin.Api.Auth;
using WinAdmin.Api.Modules;

namespace WinAdmin.Api.Controllers;

/// <summary>Управление питанием: перезагрузка, выключение, отмена.</summary>
[RequirePermission(PermissionIds.PowerManage)]
[WinAdminModule("power")]
public sealed class PowerController : WinAdminControllerBase
{
    private readonly IPowerService _power;
    private readonly IAuditService _audit;

    public PowerController(IPowerService power, IAuditService audit)
    {
        _power = power;
        _audit = audit;
    }

    /// <summary>Запланировать перезагрузку.</summary>
    [HttpPost("reboot")]
    [ProducesResponseType(typeof(OperationResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Reboot([FromBody] PowerRequest? request)
    {
        var result = _power.Reboot(request ?? new PowerRequest());
        return await AuditedAsync(_audit, "power.reboot", Environment.MachineName, result);
    }

    /// <summary>Запланировать выключение.</summary>
    [HttpPost("shutdown")]
    [ProducesResponseType(typeof(OperationResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Shutdown([FromBody] PowerRequest? request)
    {
        var result = _power.Shutdown(request ?? new PowerRequest());
        return await AuditedAsync(_audit, "power.shutdown", Environment.MachineName, result);
    }

    /// <summary>Отменить запланированное действие питания.</summary>
    [HttpPost("cancel")]
    [ProducesResponseType(typeof(OperationResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Cancel()
    {
        var result = _power.CancelPending();
        return await AuditedAsync(_audit, "power.cancel", Environment.MachineName, result);
    }
}
