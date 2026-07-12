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

    private static readonly string[] UserNameFieldPriority =
    {
        "TargetUserName", "SubjectUserName", "AccountName",
    };

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

        return $"*[System[{string.Join(" and ", conditions)}]]";
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

    /// <summary>
    /// Извлекает имя учётной записи из EventData записи (TargetUserName → SubjectUserName →
    /// AccountName, первое непустое и не "-"). Возвращает null, если запись не парсится или
    /// ни одно из полей не заполнено — вызывающий код сам решает, использовать ли fallback на SID.
    /// </summary>
    public static string? ExtractUserNameFromXml(string recordXml)
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

        var dataElements = doc.Descendants(EventNs + "Data").ToList();

        foreach (var fieldName in UserNameFieldPriority)
        {
            var value = dataElements
                .FirstOrDefault(d => (string?)d.Attribute("Name") == fieldName)
                ?.Value;
            if (!string.IsNullOrWhiteSpace(value) && value != "-")
                return value;
        }

        return null;
    }

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
