using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Security;
using WinAdmin.Api.Auth;
using WinAdmin.Api.Modules;

namespace WinAdmin.Api.Controllers;

/// <summary>Принтеры и управление очередью печати.</summary>
[WinAdminModule("printers")]
public sealed class PrintersController : WinAdminControllerBase
{
    private readonly IPrinterService _printers;
    private readonly IAuditService _audit;

    public PrintersController(IPrinterService printers, IAuditService audit)
    {
        _printers = printers;
        _audit = audit;
    }

    /// <summary>Список принтеров со статусом и числом заданий.</summary>
    [HttpGet]
    [RequirePermission(PermissionIds.PrintersRead)]
    [ProducesResponseType(typeof(IReadOnlyList<PrinterInfo>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<PrinterInfo>> Get() => Ok(_printers.GetPrinters());

    /// <summary>Управление принтером: pause, resume или purge (очистить очередь).</summary>
    [HttpPost("{name}/{action}")]
    [RequirePermission(PermissionIds.PrintersManage)]
    [ProducesResponseType(typeof(OperationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(OperationResult), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Control(string name, string action)
    {
        if (!Enum.TryParse<PrinterAction>(action, ignoreCase: true, out var parsed))
            return BadRequest(OperationResult.Fail($"Неизвестное действие '{action}'. Допустимо: pause, resume, purge"));

        var result = _printers.Control(name, parsed);
        return await AuditedAsync(_audit, $"printer.{parsed.ToString().ToLowerInvariant()}", name, result);
    }
}
