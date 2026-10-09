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

    /// <summary>URL для Kestrel:Endpoints.</summary>
    public static string ListenUrl(NetworkSettings s) => $"http://{BindAddress(s)}:{s.Port}";

    /// <summary>Адрес, по которому открывать панель после применения настроек.</summary>
    public static string PanelUrl(NetworkSettings s, string? host)
        => s.Mode == NetworkMode.Local
            ? $"http://127.0.0.1:{s.Port}"
            : $"http://{(string.IsNullOrWhiteSpace(host) ? Environment.MachineName : host)}:{s.Port}";

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
