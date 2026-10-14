using System.Globalization;
using System.Net;
using WinAdmin.Core.Models;

namespace WinAdmin.Core.Network;

/// <summary>Адреса прослушивания и панели для сетевых настроек.</summary>
public static class NetworkEndpoints
{
    public const int DefaultPort = 8080;

    public static IPAddress BindAddress(NetworkSettings s)
        => s.Mode == NetworkMode.Local ? IPAddress.Loopback : IPAddress.Any;

    public static string Scheme(NetworkSettings s) => s.HttpsEnabled ? "https" : "http";

    /// <summary>URL основного endpoint для Kestrel:Endpoints.</summary>
    public static string ListenUrl(NetworkSettings s) => $"{Scheme(s)}://{BindAddress(s)}:{s.Port}";

    /// <summary>HTTP-порт перенаправления на HTTPS (null — HTTPS выключен или порт не задан).</summary>
    public static int? RedirectPort(NetworkSettings s) => s.HttpsEnabled ? s.Https!.HttpRedirectPort : null;

    /// <summary>
    /// Ключи Kestrel:Endpoints. HTTPS — только HTTP/1.1: Negotiate (Kerberos SSO) привязан к соединению
    /// и по HTTP/2 не работает, а браузер выбрал бы HTTP/2 через ALPN. Сертификат задаётся не здесь,
    /// а через ServerCertificateSelector (пароль PFX не должен попадать в IConfiguration).
    /// </summary>
    public static IReadOnlyDictionary<string, string?> KestrelConfiguration(NetworkSettings s)
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (!s.HttpsEnabled)
        {
            data["Kestrel:Endpoints:Http:Url"] = ListenUrl(s);
            return data;
        }
        data["Kestrel:Endpoints:Https:Url"] = ListenUrl(s);
        data["Kestrel:Endpoints:Https:Protocols"] = "Http1";
        if (RedirectPort(s) is int redirect)
            data["Kestrel:Endpoints:HttpRedirect:Url"] = $"http://{BindAddress(s)}:{redirect}";
        return data;
    }

    /// <summary>Адрес, по которому открывать панель после применения настроек.</summary>
    public static string PanelUrl(NetworkSettings s, string? host)
        => s.Mode == NetworkMode.Local
            ? $"{Scheme(s)}://127.0.0.1:{s.Port}"
            : $"{Scheme(s)}://{(string.IsNullOrWhiteSpace(host) ? Environment.MachineName : host)}:{s.Port}";

    /// <summary>Порт первого адреса из значения --urls (например «http://0.0.0.0:8080»).</summary>
    public static int? PortFromUrls(string? urls)
    {
        if (string.IsNullOrWhiteSpace(urls)) return null;
        string first = urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        int scheme = first.IndexOf("://", StringComparison.Ordinal);
        string rest = scheme < 0 ? first : first[(scheme + 3)..];
        int slash = rest.IndexOf('/');
        if (slash >= 0) rest = rest[..slash];
        int colon = rest.LastIndexOf(':');
        if (colon < 0 || colon < rest.LastIndexOf(']')) return null;
        return int.TryParse(rest[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int port) ? port : null;
    }
}
