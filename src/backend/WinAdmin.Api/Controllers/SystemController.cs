using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WinAdmin.Api.Auth;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Security;
using WinAdmin.Api.Modules;

namespace WinAdmin.Api.Controllers;

/// <summary>Техническая информация о машине и live-метрики.</summary>
[WinAdminModule("system")]
public sealed class SystemController : WinAdminControllerBase
{
    private readonly ISystemInfoService _system;

    public SystemController(ISystemInfoService system) => _system = system;

    /// <summary>Сводная информация: ОС, CPU, RAM, BIOS, сеть, uptime.</summary>
    [HttpGet]
    [RequirePermission(PermissionIds.SystemRead)]
    [ProducesResponseType(typeof(SystemInfo), StatusCodes.Status200OK)]
    public ActionResult<SystemInfo> Get() => Ok(_system.GetSystemInfo());

    /// <summary>Мгновенные метрики: загрузка CPU, память, сеть.</summary>
    [HttpGet("metrics")]
    [RequirePermission(PermissionIds.SystemRead)]
    [ProducesResponseType(typeof(SystemMetrics), StatusCodes.Status200OK)]
    public ActionResult<SystemMetrics> Metrics() => Ok(_system.GetMetrics());
}
