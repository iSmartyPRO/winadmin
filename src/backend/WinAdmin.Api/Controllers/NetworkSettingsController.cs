using Microsoft.AspNetCore.Mvc;
using WinAdmin.Api.Network;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Network;
using WinAdmin.Core.Security;
using WinAdmin.Api.Auth;
using WinAdmin.Infrastructure.Network;

namespace WinAdmin.Api.Controllers;

/// <summary>
/// Сетевые настройки панели: режим (только этот компьютер / сеть), порт, разрешённые
/// подсети и HTTPS (право platform.network.manage). Применяются без перезапуска службы.
/// </summary>
[RequirePermission(PermissionIds.PlatformNetworkManage)]
[Route("api/v1/settings/network")]
public sealed class NetworkSettingsController : WinAdminControllerBase
{
    private readonly INetworkSettingsService _network;
    private readonly IHttpsCertificateLoader _certificates;
    private readonly HttpsCertificateHolder? _holder;
    private readonly NetworkApplyWatchdog _watchdog;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<NetworkSettingsController> _logger;

    public NetworkSettingsController(
        INetworkSettingsService network, IHttpsCertificateLoader certificates, NetworkApplyWatchdog watchdog,
        IServiceScopeFactory scopes, ILogger<NetworkSettingsController> logger, HttpsCertificateHolder? holder = null)
    {
        _network = network;
        _certificates = certificates;
        _holder = holder;
        _watchdog = watchdog;
        _scopes = scopes;
        _logger = logger;
    }

    /// <summary>Текущие сетевые настройки (пароль PFX не возвращается — только признак «задан»).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(NetworkSettingsDto), StatusCodes.Status200OK)]
    public ActionResult<NetworkSettingsDto> Get()
    {
        var s = _network.Current;
        return Ok(new NetworkSettingsDto(s.Mode, s.Port, s.Allow,
            NetworkEndpoints.PanelUrl(s, Request.Host.Host), s.Mode == NetworkMode.Network, HttpsDto(s.Https)));
    }

    /// <summary>Сертификаты с закрытым ключом из LocalMachine\My и CurrentUser\My.</summary>
    [HttpGet("certificates")]
    [ProducesResponseType(typeof(IReadOnlyList<CertificateInfo>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<CertificateInfo>> Certificates() => Ok(_certificates.ListStoreCertificates());

    /// <summary>
    /// Выпустить самоподписанный сертификат (keys\https.pfx): FQDN, короткое имя, localhost и
    /// переданные имена. Если HTTPS уже работает на самоподписанном — новый применяется сразу.
    /// </summary>
    [HttpPost("https/self-signed")]
    [ProducesResponseType(typeof(CertificateInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateSelfSigned([FromBody] CreateSelfSignedRequest? request, CancellationToken ct)
    {
        IReadOnlyList<string> names;
        try
        {
            var (host, domain) = MachineDnsName.Current();
            names = HttpsCertificates.SelfSignedDnsNames(host, domain, request?.DnsNames ?? []);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        var info = _certificates.CreateSelfSigned(names);
        if (_network.Current.Https?.Certificate?.Source == HttpsCertificateSource.SelfSigned)
            _holder?.Reload();

        using (var scope = _scopes.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAuditService>().WriteAsync(new AuditEntryDto
            {
                Actor = Actor,
                Action = "settings.network.https.selfsigned",
                Target = "network",
                Success = true,
                Details = $"{info.Thumbprint}: {string.Join(", ", info.DnsNames)}, до {info.NotAfter:yyyy-MM-dd}",
                SourceIp = SourceIp,
            }, ct);
        }
        return Ok(info);
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
        var https = HttpsSettingsUpdate.Merge(request.Https, previous.Https, _certificates.ProtectPfxPassword);
        var check = _network.Check(new NetworkSettings(request.Mode, request.Port, request.Allow ?? [], https));
        if (check.Errors.Count > 0)
            return BadRequest(new { message = check.Errors[0], errors = check.Errors });
        if (check.PortBusy)
            return Conflict(new { message = $"Порт {check.BusyPort} уже занят другой программой." });

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

    private HttpsSettingsDto HttpsDto(HttpsSettings? https)
    {
        var cert = https?.Certificate;
        CertificateValidation? validation = cert is null ? null : _certificates.Validate(cert);
        return new HttpsSettingsDto(
            https?.Enabled == true,
            cert?.Source,
            cert?.Thumbprint,
            cert?.StoreLocation,
            cert?.Path,
            !string.IsNullOrEmpty(cert?.Password),
            https?.HttpRedirectPort,
            validation?.Info,
            validation is { Ok: false } ? validation.Errors[0] : null);
    }

    /// <summary>Для аудита (без пароля PFX).</summary>
    private static string Describe(NetworkSettings s)
    {
        string text = s.Mode == NetworkMode.Local
            ? $"local:{s.Port}"
            : $"network:{s.Port} [{string.Join(", ", s.Allow)}]";
        if (s.HttpsEnabled)
        {
            var c = s.Https!.Certificate!;
            string source = c.Source switch
            {
                HttpsCertificateSource.Store => $"store {c.StoreLocation}\\{c.Thumbprint}",
                HttpsCertificateSource.Pfx => $"pfx {c.Path}",
                _ => "selfSigned",
            };
            text += $" https({source}{(s.Https.HttpRedirectPort is int r ? $", redirect {r}" : "")})";
        }
        return text;
    }
}
