using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Security;
using WinAdmin.Api.Auth;
using WinAdmin.Api.Modules;

namespace WinAdmin.Api.Controllers;

/// <summary>Процессы / запущенные приложения.</summary>
[WinAdminModule("processes")]
public sealed class ProcessesController : WinAdminControllerBase
{
    private readonly IProcessService _processes;
    private readonly IAuditService _audit;

    public ProcessesController(IProcessService processes, IAuditService audit)
    {
        _processes = processes;
        _audit = audit;
    }

    /// <summary>Список процессов (PID, имя, память, окно).</summary>
    [HttpGet]
    [RequirePermission(PermissionIds.ProcessesRead)]
    [ProducesResponseType(typeof(IReadOnlyList<ProcessInfo>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<ProcessInfo>> Get() => Ok(_processes.GetProcesses());

    /// <summary>Завершить процесс по PID.</summary>
    [HttpDelete("{pid:int}")]
    [RequirePermission(PermissionIds.ProcessesManage)]
    [ProducesResponseType(typeof(OperationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(OperationResult), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Kill(int pid)
    {
        var result = _processes.Kill(pid);
        return await AuditedAsync(_audit, "process.kill", pid.ToString(), result);
    }
}
