using System.Text.RegularExpressions;

namespace WinAdmin.Infrastructure.ActiveDirectory.Users;

public static partial class AdUserValidation
{
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex MailRegex();

    /// <summary>Только разрешённые атрибуты (имена приводятся к настроенному написанию), обрезка пробелов, длина, формат почты.</summary>
    public static Dictionary<string, string?> Clean(IReadOnlyDictionary<string, string?> changes, IReadOnlyCollection<string> editable)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rawName, rawValue) in changes)
        {
            string name = editable.FirstOrDefault(e => e.Equals(rawName?.Trim(), StringComparison.OrdinalIgnoreCase))
                          ?? throw new ArgumentException($"Атрибут «{rawName}» нельзя изменять.");
            string? value = string.IsNullOrWhiteSpace(rawValue) ? null : rawValue.Trim();
            int max = name.Equals("description", StringComparison.OrdinalIgnoreCase) ? 1024 : 256;
            if (value is not null && value.Length > max)
                throw new ArgumentException($"«{name}»: не длиннее {max} символов.");
            if (value is not null && name.Equals("mail", StringComparison.OrdinalIgnoreCase) && !MailRegex().IsMatch(value))
                throw new ArgumentException("«mail»: неверный адрес почты.");
            result[name] = value;
        }
        return result;
    }

    public static bool IsImage(byte[] data)
        => data.Length >= 4 && ((data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
                                || (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47));
}
