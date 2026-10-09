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
                errors.Add($"«{item}» — не IP-адрес и не подсеть CIDR (например 10.0.0.0/24).");
                badItems = true;
                continue;
            }
            if (!allow.Contains(item, StringComparer.OrdinalIgnoreCase))
                allow.Add(item);
        }

        if (input.Mode == NetworkMode.Network && allow.Count == 0 && !badItems)
            errors.Add("Для режима «Сеть» укажите хотя бы один разрешённый адрес или подсеть.");

        return (input with { Allow = allow }, errors);
    }

    private static bool IsAddressOrCidr(string value)
    {
        int slash = value.IndexOf('/');
        string addressPart = slash < 0 ? value : value[..slash];
        if (!IPAddress.TryParse(addressPart, out var ip))
            return false;
        // TryParse принимает «10» как 0.0.0.10 — требуем полную запись IPv4.
        if (ip.AddressFamily == AddressFamily.InterNetwork && addressPart.Count(c => c == '.') != 3)
            return false;
        if (slash < 0)
            return true;

        int max = ip.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32;
        return int.TryParse(value[(slash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int prefix)
            && prefix >= 1 && prefix <= max; // /0 — «любой адрес», запрещено
    }
}
