using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure.EventLogs;

namespace WinAdmin.Api.Controllers;

/// <summary>Просмотр и фильтрация журналов событий Windows.</summary>
public sealed class EventLogsController : WinAdminControllerBase
{
    private readonly IEventLogService _eventLogs;
    private readonly IExcludedUserService _excludedUsers;

    public EventLogsController(IEventLogService eventLogs, IExcludedUserService excludedUsers)
    {
        _eventLogs = eventLogs;
        _excludedUsers = excludedUsers;
    }

    /// <summary>Список всех журналов событий, доступных на этой машине.</summary>
    [HttpGet("lognames")]
    [Authorize(Policy = "scope:" + Scopes.EventLogsRead)]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<string>> GetLogNames() => Ok(_eventLogs.GetLogNames());

    /// <summary>Запрос записей журнала с фильтрами по времени, Event ID, уровню, тексту и учётной записи.</summary>
    [HttpGet("query")]
    [Authorize(Policy = "scope:" + Scopes.EventLogsRead)]
    [ProducesResponseType(typeof(EventLogQueryResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EventLogQueryResult>> Query(
        [FromQuery] string logName,
        [FromQuery] DateTime start,
        [FromQuery] DateTime end,
        CancellationToken ct,
        [FromQuery] int maxRecords = 200,
        [FromQuery] string? eventIds = null,
        [FromQuery] string? levels = null,
        [FromQuery] string? keyword = null,
        [FromQuery] string? user = null,
        [FromQuery] bool excludeSystemAccounts = false,
        [FromQuery] string? excludeLogonTypes = null)
    {
        if (string.IsNullOrWhiteSpace(logName))
            return BadRequest(new { message = "Не указан журнал (logName)." });
        if (start > end)
            return BadRequest(new { message = "Начало диапазона не может быть позже конца." });

        var request = new EventLogQueryRequest
        {
            LogName = logName,
            StartTime = start,
            EndTime = end,
            MaxRecords = Math.Clamp(maxRecords, 1, 50000),
            EventIds = EventLogQueryHelpers.ParseIntList(eventIds),
            Levels = EventLogQueryHelpers.ParseStringList(levels),
            Keyword = keyword,
            User = user,
            ExcludeSystemAccounts = excludeSystemAccounts,
            ExcludeLogonTypes = EventLogQueryHelpers.ParseIntList(excludeLogonTypes),
            ExcludeUserNames = await _excludedUsers.ListNamesAsync(ct),
        };

        try
        {
            return Ok(_eventLogs.Query(request));
        }
        catch (EventLogAccessDeniedException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (EventLogMissingException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}
