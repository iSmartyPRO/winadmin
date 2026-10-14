namespace WinAdmin.Core.Models;

/// <summary>Режим доступа к панели.</summary>
public enum NetworkMode
{
    /// <summary>Только этот компьютер (127.0.0.1).</summary>
    Local,
    /// <summary>Сеть: все интерфейсы + правило брандмауэра для перечисленных подсетей.</summary>
    Network,
}

/// <summary>Откуда брать сертификат HTTPS.</summary>
public enum HttpsCertificateSource
{
    /// <summary>Хранилище Windows «Личные» (My) по отпечатку.</summary>
    Store,
    /// <summary>Файл PFX (путь + пароль).</summary>
    Pfx,
    /// <summary>Самоподписанный сертификат, созданный WinAdmin (keys\https.pfx).</summary>
    SelfSigned,
}

/// <summary>Расположение хранилища сертификатов.</summary>
public enum CertificateStoreLocation { LocalMachine, CurrentUser }

/// <summary>
/// Сертификат HTTPS. Password — пароль PFX, зашифрованный ISecretProtector (purpose network.https.pfx);
/// открытым текстом не хранится.
/// </summary>
public sealed record HttpsCertificateSettings(
    HttpsCertificateSource Source,
    string? Thumbprint = null,
    CertificateStoreLocation? StoreLocation = null,
    string? Path = null,
    string? Password = null);

/// <summary>HTTPS основного порта и необязательный HTTP-порт перенаправления (308).</summary>
public sealed record HttpsSettings(bool Enabled, HttpsCertificateSettings? Certificate = null, int? HttpRedirectPort = null)
{
    public static HttpsSettings Disabled { get; } = new(false);
}

/// <summary>Сетевые настройки панели (содержимое network.json).</summary>
public sealed record NetworkSettings(NetworkMode Mode, int Port, IReadOnlyList<string> Allow, HttpsSettings? Https = null)
{
    public static NetworkSettings Default { get; } = new(NetworkMode.Local, 8080, []);

    /// <summary>Основной порт обслуживается по HTTPS.</summary>
    public bool HttpsEnabled => Https is { Enabled: true };

    /// <summary>Сравнение по содержимому (record сравнивает списки по ссылке).</summary>
    public bool IsEquivalentTo(NetworkSettings other)
        => Mode == other.Mode && Port == other.Port && (Allow ?? []).SequenceEqual(other.Allow ?? [])
           && Equals(Https ?? HttpsSettings.Disabled, other.Https ?? HttpsSettings.Disabled);
}

/// <summary>Текущие сетевые настройки для UI.</summary>
public sealed record NetworkSettingsDto(NetworkMode Mode, int Port, IReadOnlyList<string> Allow, string Url, bool FirewallRule);

/// <summary>Запрос на изменение сетевых настроек.</summary>
public sealed record UpdateNetworkSettingsRequest(NetworkMode Mode, int Port, List<string>? Allow);

/// <summary>Результат изменения: новый адрес панели.</summary>
public sealed record NetworkUpdateResult(string Url);
