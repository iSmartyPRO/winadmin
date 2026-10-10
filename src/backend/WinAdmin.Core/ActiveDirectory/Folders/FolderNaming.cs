using System.Text.RegularExpressions;

namespace WinAdmin.Core.ActiveDirectory.Folders;

public static partial class FolderNaming
{
    [GeneratedRegex("^[A-Za-z0-9_-]{1,40}$")]
    private static partial Regex NamePart();

    /// <summary>Строки «A=\\fs01\Projects» (или «A:=…») → буква → UNC без завершающего «\».</summary>
    public static IReadOnlyDictionary<char, string> ParseMappings(IEnumerable<string> lines)
    {
        var map = new Dictionary<char, string>();
        foreach (var raw in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            int eq = raw.IndexOf('=');
            string drive = eq < 0 ? "" : raw[..eq].Trim().TrimEnd(':');
            string unc = eq < 0 ? "" : raw[(eq + 1)..].Trim().TrimEnd('\\');
            if (drive.Length != 1 || !char.IsLetter(drive[0]) || !unc.StartsWith(@"\\") || unc.Length < 5)
                throw new ArgumentException($"Сопоставление «{raw}»: ожидается вида A=\\\\сервер\\шара.");
            map[char.ToUpperInvariant(drive[0])] = unc;
        }
        return map;
    }

    /// <summary>Путь из описания группы → UNC. Несопоставленная буква, «..» или не путь — ArgumentException.</summary>
    public static string ToUnc(string path, IReadOnlyDictionary<char, string> mappings)
    {
        string p = path.Trim().TrimEnd('\\');
        if (p.Split('\\', '/').Any(s => s == ".."))
            throw new ArgumentException("Путь не должен содержать «..».");
        if (p.StartsWith(@"\\"))
        {
            if (p.Split('\\', StringSplitOptions.RemoveEmptyEntries).Length < 2)
                throw new ArgumentException(@"UNC-путь должен быть вида \\сервер\шара\….");
            return p;
        }
        if (p.Length >= 2 && char.IsLetter(p[0]) && p[1] == ':')
        {
            char drive = char.ToUpperInvariant(p[0]);
            if (!mappings.TryGetValue(drive, out var unc))
                throw new ArgumentException($"Буква диска {drive}: не сопоставлена с сетевым путём (настройки модуля «Папки»).");
            return unc + p[2..];
        }
        throw new ArgumentException("Путь должен быть вида A:\\… или \\\\сервер\\шара\\….");
    }

    /// <summary>Самая частая вторая часть имён групп проекта «sg_&lt;org&gt;_…»; иначе — имя проекта латиницей; иначе null.</summary>
    public static string? DefaultOrgCode(string projectName, IEnumerable<string> groupNames, string prefix)
    {
        var fromGroups = groupNames
            .Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(n => n[prefix.Length..].Split('_', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
            .Where(s => !string.IsNullOrEmpty(s))
            .GroupBy(s => s!.ToLowerInvariant())
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();
        if (fromGroups is not null) return fromGroups;
        string fromName = new string(projectName.ToLowerInvariant().Where(c => !char.IsWhiteSpace(c)).ToArray());
        return NamePart().IsMatch(fromName) ? fromName : null;
    }

    public static (string Full, string Read) GroupNames(string prefix, string orgCode, string baseName)
    {
        if (!NamePart().IsMatch(orgCode)) throw new ArgumentException("Код организации — латиница, цифры, «_» или «-».");
        if (!NamePart().IsMatch(baseName)) throw new ArgumentException("Имя папки для групп — латиница, цифры, «_» или «-», до 40 символов.");
        string full = $"{prefix}{orgCode}_{baseName}_full".ToLowerInvariant();
        string read = $"{prefix}{orgCode}_{baseName}_read".ToLowerInvariant();
        if (full.Length > 64) throw new ArgumentException("Имя группы длиннее 64 символов — сократите имя.");
        return (full, read);
    }
}
