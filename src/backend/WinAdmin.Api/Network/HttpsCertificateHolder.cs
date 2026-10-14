using System.Security.Cryptography.X509Certificates;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;

namespace WinAdmin.Api.Network;

/// <summary>
/// Действующие сетевые настройки и сертификат HTTPS для Kestrel (ServerCertificateSelector).
/// Обновляется провайдером конфигурации при каждом перечитывании network.json — до перепривязки.
/// </summary>
public sealed class HttpsCertificateHolder(IHttpsCertificateLoader loader)
{
    private readonly Lock _lock = new();
    private volatile NetworkSettings _settings = NetworkSettings.Default;
    private volatile X509Certificate2? _certificate;
    private HttpsCertificateSettings? _loadedFor;

    /// <summary>Настройки, по которым сейчас настроен Kestrel.</summary>
    public NetworkSettings Settings => _settings;

    /// <summary>Сертификат для TLS-рукопожатия (null — HTTPS выключен или сертификат не загрузился).</summary>
    public X509Certificate2? Certificate => _certificate;

    /// <summary>Журнал (назначается после построения приложения: holder создаётся раньше DI).</summary>
    public ILogger? Logger { get; set; }

    /// <summary>Почему сертификат не загрузился.</summary>
    public string? Error { get; private set; }

    public void Update(NetworkSettings settings)
    {
        lock (_lock)
        {
            _settings = settings;
            var wanted = settings.HttpsEnabled ? settings.Https!.Certificate : null;
            if (Equals(wanted, _loadedFor) && (_certificate is not null || wanted is null))
                return;
            LoadLocked(wanted);
        }
    }

    /// <summary>Перечитать сертификат (например, после выпуска нового самоподписанного).</summary>
    public void Reload()
    {
        lock (_lock)
            LoadLocked(_settings.HttpsEnabled ? _settings.Https!.Certificate : null);
    }

    private void LoadLocked(HttpsCertificateSettings? wanted)
    {
        _loadedFor = wanted;
        // Прежний сертификат не освобождаем: им может идти рукопожатие на другом потоке.
        if (wanted is null)
        {
            _certificate = null;
            Error = null;
            return;
        }
        var result = loader.Load(wanted);
        _certificate = result.Certificate;
        Error = result.Error;
        if (result.Error is not null)
            Logger?.LogError("HTTPS: {Error}", result.Error);
    }
}
