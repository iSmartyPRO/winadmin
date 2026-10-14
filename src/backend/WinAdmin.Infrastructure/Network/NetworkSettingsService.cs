using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Network;

namespace WinAdmin.Infrastructure.Network;

public sealed class NetworkSettingsService : INetworkSettingsService
{
    private readonly NetworkSettingsStore _store;
    private readonly IFirewallRunner _firewall;
    private readonly IPortProbe _probe;
    private readonly IHttpsCertificateLoader? _certificates;

    public NetworkSettingsService(NetworkSettingsStore store, IFirewallRunner firewall, IPortProbe probe,
        IHttpsCertificateLoader? certificates = null)
    {
        _store = store;
        _firewall = firewall;
        _probe = probe;
        _certificates = certificates;
    }

    public NetworkSettings Current => _store.ReadOrDefault(out _);

    public NetworkCheckResult Check(NetworkSettings next)
    {
        var (normalized, normalizeErrors) = NetworkSettingsValidator.Normalize(next);
        var errors = normalizeErrors.ToList();
        if (errors.Count == 0 && normalized.HttpsEnabled)
        {
            if (_certificates is null)
                errors.Add("Проверка сертификата HTTPS недоступна.");
            else
                errors.AddRange(_certificates.Validate(normalized.Https!.Certificate!).Errors);
        }
        if (errors.Count > 0)
            return new NetworkCheckResult(normalized, errors, null);

        // Порты, которые сейчас слушаем мы сами, освободятся при перепривязке — их не проверяем.
        var current = Current;
        int?[] ours = [current.Port, NetworkEndpoints.RedirectPort(current)];
        int? busy = new[] { normalized.Port, NetworkEndpoints.RedirectPort(normalized) }
            .OfType<int>()
            .Where(p => !ours.Contains(p))
            .Select(p => (int?)p)
            .FirstOrDefault(p => _probe.IsInUse(p!.Value));
        return new NetworkCheckResult(normalized, errors, busy);
    }

    public void Apply(NetworkSettings next, NetworkSettings previous)
    {
        try
        {
            ApplyFirewall(next, [previous.Port]);
        }
        catch
        {
            // Команды удаляют старое правило до создания нового — без восстановления
            // неудачное добавление оставило бы режим «Сеть» вообще без правила.
            try { ApplyFirewall(previous, [next.Port]); } catch { /* исходная ошибка важнее */ }
            throw;
        }
        _store.Write(next);
    }

    public void ApplyFirewall(NetworkSettings settings, IEnumerable<int> legacyPorts)
        => _firewall.Run(FirewallCommands.Build(settings, legacyPorts));
}
