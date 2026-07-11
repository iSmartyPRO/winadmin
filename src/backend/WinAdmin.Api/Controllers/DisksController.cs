using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Security;

namespace WinAdmin.Api.Controllers;

/// <summary>Физические диски с разделами и заполнением.</summary>
public sealed class DisksController : WinAdminControllerBase
{
    private readonly IDiskService _disks;

    public DisksController(IDiskService disks) => _disks = disks;

    /// <summary>Дерево «диск → тома» с размерами и свободным местом.</summary>
    [HttpGet]
    [Authorize(Policy = "scope:" + Scopes.DisksRead)]
    [ProducesResponseType(typeof(IReadOnlyList<PhysicalDisk>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<PhysicalDisk>> Get() => Ok(_disks.GetDisks());
}
