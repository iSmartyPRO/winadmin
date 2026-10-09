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

        return (input with { Allow = allow }, errors);
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
