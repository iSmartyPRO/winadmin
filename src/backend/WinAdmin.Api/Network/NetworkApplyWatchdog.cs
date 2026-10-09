using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Network;
using WinAdmin.Infrastructure.Network;

namespace WinAdmin.Api.Network;

/// <summary>
/// После применения сетевых настроек проверяет, что Kestrel действительно слушает новый
/// endpoint; если нет — возвращает прежние настройки (защита от потери доступа).
/// </summary>
public sealed class NetworkApplyWatchdog
{
    private readonly INetworkSettingsService _network;
    private readonly IPortProbe _probe;
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _config;
    private readonly ILogger<NetworkApplyWatchdog> _logger;

    public NetworkApplyWatchdog(
        INetworkSettingsService network, IPortProbe probe, IServiceScopeFactory scopes,
        IConfiguration config, ILogger<NetworkApplyWatchdog> logger)
    {
        _network = network;
        _probe = probe;
        _scopes = scopes;
        _config = config;
        _logger = logger;
    }

    public Task Schedule(NetworkSettings previous, NetworkSettings applied, string actor, string? sourceIp)
        => Task.Run(async () =>
        {
            int delay = _config.GetValue("WinAdmin:Network:VerifyDelaySeconds", 10);
            await Task.Delay(TimeSpan.FromSeconds(delay));
            if (_probe.IsListening(applied))
                return;
            // За время ожидания настройки могли снова изменить (CLI, второй запрос) —
            // откатывать можно только то, что применяли сами.
            if (!_network.Current.IsEquivalentTo(applied))
                return;

            _logger.LogError("Панель не слушает {Url} после применения настроек — откат к {Previous}.",
                NetworkEndpoints.ListenUrl(applied), NetworkEndpoints.ListenUrl(previous));
            string details = $"Не удалось начать прослушивание {NetworkEndpoints.ListenUrl(applied)}; возвращено {NetworkEndpoints.ListenUrl(previous)}";
            try
            {
                _network.Apply(previous, applied);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Откат сетевых настроек не удался.");
                details += $"; ошибка отката: {ex.Message}";
            }

            try
            {
                using var scope = _scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IAuditService>().WriteAsync(new AuditEntryDto
                {
                    Actor = actor,
                    Action = "settings.network.rollback",
                    Target = "network",
                    Success = false,
                    Details = details,
                    SourceIp = sourceIp,
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Не удалось записать откат сетевых настроек в аудит.");
            }
        });
}
