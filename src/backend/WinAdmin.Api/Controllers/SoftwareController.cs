using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Security;
using WinAdmin.Api.Auth;
using WinAdmin.Api.Modules;

namespace WinAdmin.Api.Controllers;

[WinAdminModule("software")]
public sealed class SoftwareController : WinAdminControllerBase
{
    private readonly ISoftwareCatalogService _catalog;
    private readonly ISoftwareJobService _jobs;

    public SoftwareController(ISoftwareCatalogService catalog, ISoftwareJobService jobs)
    {
        _catalog = catalog;
        _jobs = jobs;
    }

    [HttpGet("applications")]
    [RequirePermission(PermissionIds.SoftwareRead)]
    [ProducesResponseType(typeof(IReadOnlyList<InstalledApp>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<InstalledApp>> GetApplications()
        => Ok(_catalog.GetApplications());

    [HttpPost("applications/{id}/uninstall")]
    [RequirePermission(PermissionIds.SoftwareManage)]
    [ProducesResponseType(typeof(SoftwareJob), StatusCodes.Status202Accepted)]
    public IActionResult UninstallApplication(string id)
        => StartJob(() => _jobs.StartUninstallApp(Uri.UnescapeDataString(id)));

    [HttpGet("updates")]
    [RequirePermission(PermissionIds.SoftwareRead)]
    [ProducesResponseType(typeof(IReadOnlyList<InstalledUpdate>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<InstalledUpdate>> GetUpdates()
        => Ok(_catalog.GetUpdates());

    [HttpPost("updates/{id}/uninstall")]
    [RequirePermission(PermissionIds.SoftwareManage)]
    [ProducesResponseType(typeof(SoftwareJob), StatusCodes.Status202Accepted)]
    public IActionResult UninstallUpdate(string id)
        => StartJob(() => _jobs.StartUninstallUpdate(Uri.UnescapeDataString(id)));

    [HttpPost("updates/{id}/rollback")]
    [RequirePermission(PermissionIds.SoftwareManage)]
    [ProducesResponseType(typeof(SoftwareJob), StatusCodes.Status202Accepted)]
    public IActionResult RollbackUpdate(string id)
        => StartJob(() => _jobs.StartRollbackUpdate(Uri.UnescapeDataString(id)));

    [HttpGet("jobs/active")]
    [RequirePermission(PermissionIds.SoftwareManage)]
    [ProducesResponseType(typeof(SoftwareJob), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult GetActiveJob()
    {
        var job = _jobs.GetActiveJob();
        return job is null ? NoContent() : Ok(job);
    }

    [HttpGet("jobs/{jobId}")]
    [RequirePermission(PermissionIds.SoftwareManage)]
    [ProducesResponseType(typeof(SoftwareJob), StatusCodes.Status200OK)]
    public ActionResult<SoftwareJob> GetJob(string jobId)
    {
        var job = _jobs.GetJob(jobId);
        return job is null ? NotFound() : Ok(job);
    }

    private IActionResult StartJob(Func<SoftwareJob> start)
    {
        try
        {
            var job = start();
            return Accepted(job);
        }
        catch (SoftwareNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (SoftwareActionNotAllowedException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (SoftwareConflictException ex)
        {
            return Conflict(new { message = ex.Message, activeJobId = ex.ActiveJobId });
        }
    }
}
