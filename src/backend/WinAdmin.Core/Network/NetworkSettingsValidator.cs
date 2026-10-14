using System.Globalization;
using System.Net;
using System.Net.Sockets;
using WinAdmin.Core.Models;

namespace WinAdmin.Core.Network;

/// <summary>Нормализация и проверка сетевых настроек.</summary>
public static class NetworkSettingsValidator
{
    public static (NetworkSettings Settings, IReadOnlyList<string> Errors) Normalize(NetworkSettings input)
    {
        var errors = new List<string>();
        if (!Enum.IsDefined(input.Mode))
            errors.Add("Неизвестный режим доступа.");
        if (input.Port is < 1 or > 65535)
            errors.Add("Порт должен быть от 1 до 65535.");

        var allow = new List<string>();
        bool badItems = false;
        foreach (var raw in input.Allow ?? [])
        {
            string item = raw?.Trim() ?? "";
            if (item.Length == 0) continue;
            if (!IsAddressOrCidr(item))
            {
                errors.Add($"«{item}» — не IPv4-адрес и не подсеть CIDR (например 10.0.0.0/24).");
                badItems = true;
                continue;
            }
            if (!allow.Contains(item, StringComparer.OrdinalIgnoreCase))
                allow.Add(item);
        }

        if (input.Mode == NetworkMode.Network && allow.Count == 0 && !badItems)
            errors.Add("Для режима «Сеть» укажите хотя бы один разрешённый адрес или подсеть.");

        var https = NormalizeHttps(input.Https, input.Port, errors);
        return (input with { Allow = allow, Https = https }, errors);
    }

    /// <summary>
    /// Отпечаток сертификата в верхнем регистре без пробелов, двоеточий и невидимых символов
    /// (оснастка «Сертификаты» копирует отпечаток с U+200E в начале).
    /// </summary>
    public static string NormalizeThumbprint(string? raw)
        => new string((raw ?? "")
                .Where(c => !char.IsWhiteSpace(c) && c != ':' && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.Format)
                .ToArray())
            .ToUpperInvariant();

    private static HttpsSettings? NormalizeHttps(HttpsSettings? https, int port, List<string> errors)
    {
        // Выключенный HTTPS хранится как есть (чтобы переключение туда-обратно не теряло сертификат).
        if (https is not { Enabled: true }) return https;

        var cert = https.Certificate;
        if (cert is null || !Enum.IsDefined(cert.Source))
        {
            errors.Add("Для HTTPS выберите сертификат: из хранилища, файл PFX или самоподписанный.");
            return https;
        }

        switch (cert.Source)
        {
            case HttpsCertificateSource.Store:
                string thumb = NormalizeThumbprint(cert.Thumbprint);
                if (thumb.Length != 40 || !thumb.All(Uri.IsHexDigit))
                    errors.Add("Отпечаток сертификата — 40 шестнадцатеричных символов (например 3A1B…).");
                cert = cert with { Thumbprint = thumb, StoreLocation = cert.StoreLocation ?? CertificateStoreLocation.LocalMachine, Path = null, Password = null };
                break;
            case HttpsCertificateSource.Pfx:
                string path = cert.Path?.Trim() ?? "";
                if (path.Length == 0 || !System.IO.Path.IsPathFullyQualified(path))
                    errors.Add("Укажите полный путь к файлу PFX (например C:\\certs\\panel.pfx).");
                cert = cert with { Path = path, Thumbprint = null, StoreLocation = null };
                break;
            case HttpsCertificateSource.SelfSigned:
                cert = new HttpsCertificateSettings(HttpsCertificateSource.SelfSigned);
                break;
        }

        if (https.HttpRedirectPort is int redirect && (redirect is < 1 or > 65535 || redirect == port))
            errors.Add("Порт перенаправления HTTP должен быть от 1 до 65535 и отличаться от основного порта.");

        return https with { Certificate = cert };
    }

    private static bool IsAddressOrCidr(string value)
    {
        int slash = value.IndexOf('/');
        string addressPart = slash < 0 ? value : value[..slash];
        // Только IPv4: режим «Сеть» слушает 0.0.0.0, правило для IPv6 ничего бы не защищало.
        if (!IPAddress.TryParse(addressPart, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
            return false;
        // Только каноническая запись: TryParse принимает «10» (0.0.0.10), восьмеричные и
        // шестнадцатеричные октеты — такие строки неочевидны и не всегда понятны netsh.
        if (ip.ToString() != addressPart)
            return false;
        if (slash < 0)
            return true;

        return int.TryParse(value[(slash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int prefix)
            && prefix >= 1 && prefix <= 32; // /0 — «любой адрес», запрещено
    }
}
