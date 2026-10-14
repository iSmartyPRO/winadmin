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
    private readonly ITlsProbe? _tls;

    public NetworkApplyWatchdog(
        INetworkSettingsService network, IPortProbe probe, IServiceScopeFactory scopes,
        IConfiguration config, ILogger<NetworkApplyWatchdog> logger, ITlsProbe? tls = null)
    {
        _tls = tls;
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
            string? failure = !_probe.IsListening(applied)
                ? $"Не удалось начать прослушивание {NetworkEndpoints.ListenUrl(applied)}"
                // «Порт слушается» не значит, что TLS работает: сертификат мог не загрузиться.
                : applied.HttpsEnabled && _tls is not null && await _tls.HandshakeAsync(applied.Port, CancellationToken.None) is null
                    ? $"TLS-рукопожатие с {NetworkEndpoints.ListenUrl(applied)} не удалось (сертификат не загружен?)"
                    : null;
            if (failure is null)
                return;
            // За время ожидания настройки могли снова изменить (CLI, второй запрос) —
            // откатывать можно только то, что применяли сами.
            if (!_network.Current.IsEquivalentTo(applied))
                return;

            _logger.LogError("{Failure} — откат к {Previous}.", failure, NetworkEndpoints.ListenUrl(previous));
            string details = $"{failure}; возвращено {NetworkEndpoints.ListenUrl(previous)}";
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
