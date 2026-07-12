using System.Xml.Linq;
using WinAdmin.Core.Models;

namespace WinAdmin.Infrastructure.EventLogs;

/// <summary>
/// Логика построения XPath-фильтра и разбора XML записи журнала — без зависимости от
/// реального Event Log API, чтобы её можно было полноценно покрыть unit-тестами.
/// </summary>
public static class EventLogQueryHelpers
{
    private static readonly XNamespace EventNs = "http://schemas.microsoft.com/win/2004/08/events/event";

    private static readonly string[] UserXPathFields = ["TargetUserName", "SubjectUserName", "AccountName"];

    public static string BuildXPathFilter(EventLogQueryRequest request)
    {
        var conditions = new List<string>
        {
            $"TimeCreated[@SystemTime>='{FormatXPathTime(request.StartTime)}' and @SystemTime<='{FormatXPathTime(request.EndTime)}']",
        };

        if (request.EventIds is { Count: > 0 })
        {
            var ids = string.Join(" or ", request.EventIds.Select(id => $"EventID={id}"));
            conditions.Add($"({ids})");
        }

        if (request.Levels is { Count: > 0 })
        {
            var levels = string.Join(" or ", request.Levels.Select(l => $"Level={LevelToNumber(l)}"));
            conditions.Add($"({levels})");
        }

        var systemPart = $"System[{string.Join(" and ", conditions)}]";
        var user = NormalizeUser(request.User);
        if (user is null)
            return $"*[{systemPart}]";

        var escaped = EscapeXPathLiteral(user);
        var userMatch = string.Join(" or ", UserXPathFields.Select(f => $"Data[@Name='{f}']={escaped}"));
        return $"*[{systemPart} and EventData[{userMatch}]]";
    }

    /// <summary>Trim; пустая строка → null (фильтр по пользователю не применяется).</summary>
    private static string? NormalizeUser(string? user)
    {
        var trimmed = user?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>Экранирует литерал для XPath: XML-сущности и выбор кавычек.</summary>
    private static string EscapeXPathLiteral(string value)
    {
        var escaped = value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

        if (escaped.Contains('\''))
            return $"\"{escaped.Replace("\"", "&quot;", StringComparison.Ordinal)}\"";

        return $"'{escaped}'";
    }

    public static string FormatXPathTime(DateTime dt) =>
        dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

    public static int LevelToNumber(string level) => level switch
    {
        "Critical" => 1,
        "Error" => 2,
        "Warning" => 3,
        "Information" => 4,
        _ => throw new ArgumentException($"Неизвестный уровень: {level}", nameof(level)),
    };

    private static readonly (string NameField, string? SidField)[] UserFieldPriority =
    {
        ("TargetUserName", "TargetUserSid"),
        ("SubjectUserName", "SubjectUserSid"),
        ("AccountName", null),
    };

    private const string SidSystem = "S-1-5-18";
    private const string SidLocalService = "S-1-5-19";
    private const string SidNetworkService = "S-1-5-20";

    /// <summary>
    /// Извлекает (имя, SID) учётной записи из EventData записи, перебирая пары полей
    /// TargetUserName/TargetUserSid → SubjectUserName/SubjectUserSid → AccountName (без SID).
    /// Останавливается на первом непустом и не "-" имени; SID берётся из парного поля той же
    /// записи, если для этого приоритета оно объявлено.
    /// </summary>
    public static (string? Name, string? Sid) ExtractUserInfo(string recordXml)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(recordXml);
        }
        catch (System.Xml.XmlException)
        {
            return (null, null);
        }

        var dataElements = doc.Descendants(EventNs + "Data").ToList();

        string? FieldValue(string fieldName) =>
            dataElements.FirstOrDefault(d => (string?)d.Attribute("Name") == fieldName)?.Value;

        foreach (var (nameField, sidField) in UserFieldPriority)
        {
            var name = FieldValue(nameField);
            if (string.IsNullOrWhiteSpace(name) || name == "-") continue;
            var sid = sidField != null ? FieldValue(sidField) : null;
            return (name, string.IsNullOrWhiteSpace(sid) ? null : sid);
        }

        return (null, null);
    }

    /// <summary>
    /// Извлекает тип входа (LogonType) из EventData записи. Возвращает null, если поля нет
    /// (события без входа — например 4672 или изменения учётных записей) или оно не числовое.
    /// </summary>
    public static int? ExtractLogonType(string recordXml)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(recordXml);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }

        var value = doc.Descendants(EventNs + "Data")
            .FirstOrDefault(d => (string?)d.Attribute("Name") == "LogonType")?.Value;

        return int.TryParse(value, out var logonType) ? logonType : null;
    }

    /// <summary>
    /// Извлекает IP-адрес источника входа из EventData (поле IpAddress). Возвращает null,
    /// если поля нет, оно пустое или "-" (локальный вход без сетевого источника).
    /// </summary>
    public static string? ExtractIpAddress(string recordXml)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(recordXml);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }

        var value = doc.Descendants(EventNs + "Data")
            .FirstOrDefault(d => (string?)d.Attribute("Name") == "IpAddress")?.Value;

        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed == "-" ? null : trimmed;
    }

    /// <summary>
    /// Определяет встроенную системную учётную запись по SID (языконезависимо — SID не
    /// переводится): SYSTEM/LOCAL SERVICE/NETWORK SERVICE, либо по суффиксу "$" в имени
    /// (машинный аккаунт — соглашение именования NetBIOS, тоже не зависит от языка). Если SID
    /// недоступен и имя не оканчивается на "$", запись не считается системной — по
    /// локализованному имени не гадаем.
    /// </summary>
    public static bool IsSystemAccount(string? sid, string? name)
    {
        if (sid is SidSystem or SidLocalService or SidNetworkService) return true;
        return name is not null && name.EndsWith('$');
    }

    /// <summary>
    /// Проверяет, входит ли учётная запись события в чёрный список (регистронезависимо).
    /// Сравнивает как полное извлечённое имя (например «DOMAIN\svc» или «svc»), так и часть
    /// после последнего «\», чтобы имя из настроек совпадало и с доменно-квалифицированным
    /// вариантом. Ожидается, что <paramref name="excluded"/> создан с
    /// StringComparer.OrdinalIgnoreCase.
    /// </summary>
    public static bool IsExcludedUser(string? user, IReadOnlySet<string> excluded)
    {
        if (string.IsNullOrEmpty(user) || excluded.Count == 0) return false;
        if (excluded.Contains(user)) return true;

        var slash = user.LastIndexOf('\\');
        return slash >= 0 && slash < user.Length - 1 && excluded.Contains(user[(slash + 1)..]);
    }

    public static bool MatchesSubstring(string? haystack, string? needle) =>
        string.IsNullOrEmpty(needle) || (haystack?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false);

    public static IReadOnlyList<int>? ParseIntList(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return null;
        var result = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => int.TryParse(s, out _))
            .Select(int.Parse)
            .ToList();
        return result.Count > 0 ? result : null;
    }

    public static IReadOnlyList<string>? ParseStringList(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return null;
        var result = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        return result.Count > 0 ? result : null;
    }
}
