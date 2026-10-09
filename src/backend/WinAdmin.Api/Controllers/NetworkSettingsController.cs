using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WinAdmin.Api.Network;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Network;
using WinAdmin.Core.Security;

namespace WinAdmin.Api.Controllers;

/// <summary>
/// Сетевые настройки панели: режим (только этот компьютер / сеть), порт и разрешённые
/// подсети (требует scope admin). Применяются без перезапуска службы.
/// </summary>
[Authorize(Policy = "scope:" + Scopes.Admin)]
[Route("api/v1/settings/network")]
public sealed class NetworkSettingsController : WinAdminControllerBase
{
    private readonly INetworkSettingsService _network;
    private readonly NetworkApplyWatchdog _watchdog;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<NetworkSettingsController> _logger;

    public NetworkSettingsController(
        INetworkSettingsService network, NetworkApplyWatchdog watchdog,
        IServiceScopeFactory scopes, ILogger<NetworkSettingsController> logger)
    {
        _network = network;
        _watchdog = watchdog;
        _scopes = scopes;
        _logger = logger;
    }

    /// <summary>Текущие сетевые настройки.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(NetworkSettingsDto), StatusCodes.Status200OK)]
    public ActionResult<NetworkSettingsDto> Get()
    {
        var s = _network.Current;
        return Ok(new NetworkSettingsDto(s.Mode, s.Port, s.Allow,
            NetworkEndpoints.PanelUrl(s, Request.Host.Host), s.Mode == NetworkMode.Network));
    }

    /// <summary>
    /// Изменить сетевые настройки. Ответ содержит новый адрес панели; применение
    /// (брандмауэр + перепривязка) происходит сразу после отправки ответа.
    /// </summary>
    [HttpPut]
    [ProducesResponseType(typeof(NetworkUpdateResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public IActionResult Update([FromBody] UpdateNetworkSettingsRequest request)
    {
        var previous = _network.Current;
        var check = _network.Check(new NetworkSettings(request.Mode, request.Port, request.Allow ?? []));
        if (check.Errors.Count > 0)
            return BadRequest(new { message = check.Errors[0], errors = check.Errors });
        if (check.PortBusy)
            return Conflict(new { message = $"Порт {check.Normalized.Port} уже занят другой программой." });

        var next = check.Normalized;
        string url = NetworkEndpoints.PanelUrl(next, Request.Host.Host);
        if (next.IsEquivalentTo(previous))
            return Ok(new NetworkUpdateResult(url));

        string actor = Actor;
        string? sourceIp = SourceIp;
        Response.OnCompleted(async () =>
        {
            bool success = true;
            string details = $"{Describe(previous)} → {Describe(next)}";
            try
            {
                _network.Apply(next, previous);
            }
            catch (Exception ex)
            {
                success = false;
                details += $": {ex.Message}";
                _logger.LogError(ex, "Не удалось применить сетевые настройки.");
            }

            using (var scope = _scopes.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IAuditService>().WriteAsync(new AuditEntryDto
                {
                    Actor = actor,
                    Action = "settings.network",
                    Target = "network",
                    Success = success,
                    Details = details,
                    SourceIp = sourceIp,
                });
            }

            if (success)
                _ = _watchdog.Schedule(previous, next, actor, sourceIp);
        });

        return Ok(new NetworkUpdateResult(url));
    }

    private static string Describe(NetworkSettings s)
        => s.Mode == NetworkMode.Local
            ? $"local:{s.Port}"
            : $"network:{s.Port} [{string.Join(", ", s.Allow)}]";
}
