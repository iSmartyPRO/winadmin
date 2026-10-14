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

    /// <summary>Основной порт обслуживается по HTTPS (вычисляется, в network.json не пишется).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HttpsEnabled => Https is { Enabled: true };

    /// <summary>Сравнение по содержимому (record сравнивает списки по ссылке).</summary>
    public bool IsEquivalentTo(NetworkSettings other)
        => Mode == other.Mode && Port == other.Port && (Allow ?? []).SequenceEqual(other.Allow ?? [])
           && Equals(Https ?? HttpsSettings.Disabled, other.Https ?? HttpsSettings.Disabled);
}

/// <summary>Текущие сетевые настройки для UI.</summary>
public sealed record NetworkSettingsDto(NetworkMode Mode, int Port, IReadOnlyList<string> Allow, string Url, bool FirewallRule,
    HttpsSettingsDto? Https = null);

/// <summary>Настройки HTTPS для UI: пароль PFX наружу не отдаётся — только «задан/не задан».</summary>
public sealed record HttpsSettingsDto(
    bool Enabled,
    HttpsCertificateSource? Source,
    string? Thumbprint,
    CertificateStoreLocation? StoreLocation,
    string? PfxPath,
    bool PfxPasswordSet,
    int? HttpRedirectPort,
    CertificateInfo? Certificate,
    string? CertificateError);

/// <summary>Запрос на изменение сетевых настроек (Https = null — HTTPS не меняется).</summary>
public sealed record UpdateNetworkSettingsRequest(NetworkMode Mode, int Port, List<string>? Allow, UpdateHttpsRequest? Https = null);

/// <summary>
/// Изменение HTTPS. Source = null — прежний сертификат; PfxPassword = null — прежний пароль
/// (пустая строка — PFX без пароля); HttpRedirectPort null или 0 — без перенаправления.
/// </summary>
public sealed record UpdateHttpsRequest(
    bool Enabled,
    HttpsCertificateSource? Source = null,
    string? Thumbprint = null,
    CertificateStoreLocation? StoreLocation = null,
    string? PfxPath = null,
    string? PfxPassword = null,
    int? HttpRedirectPort = null);

/// <summary>Запрос на выпуск самоподписанного сертификата (дополнительные DNS-имена).</summary>
public sealed record CreateSelfSignedRequest(List<string>? DnsNames);

/// <summary>Результат изменения: новый адрес панели.</summary>
public sealed record NetworkUpdateResult(string Url);

/// <summary>Сведения о сертификате HTTPS (без секретов).</summary>
public sealed record CertificateInfo(
    string Thumbprint,
    string Subject,
    string Issuer,
    IReadOnlyList<string> DnsNames,
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter,
    bool HasPrivateKey,
    CertificateStoreLocation? StoreLocation = null);
