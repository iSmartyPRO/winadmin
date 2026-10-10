namespace WinAdmin.Core.ActiveDirectory.Folders;

/// <summary>Порт parseFolderFromGroup (Access): «путь;тип доступа» в description (или info), тип — иначе по суффиксу имени.</summary>
public static class FolderDescriptionParser
{
    public static ParsedFolder? Parse(string? description, string? info, string groupName)
    {
        string text = (!string.IsNullOrWhiteSpace(description) ? description : info ?? "").Trim();
        if (text.Length == 0) return null;

        var parts = text.Split(';').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        int index = parts.FindIndex(IsPath);
        if (index < 0) return null;

        string path = parts[index];
        string candidate = string.Join(" ", parts.Skip(index + 1)).Trim();
        var (access, accessText) = Normalize(candidate);
        if (access is null)
        {
            var segments = Key(groupName).Split('_', StringSplitOptions.RemoveEmptyEntries);
            string last = segments.LastOrDefault() ?? "";
            string last2 = string.Join("_", segments.TakeLast(2));
            if (last is "full" or "f" || last2 == "full_access") access = FolderAccess.Full;
            else if (last is "read" or "r" or "ro" || last2 == "read_only") access = FolderAccess.Read;
        }
        return new ParsedFolder(path, access ?? FolderAccess.Other, accessText);
    }

    /// <summary>Ключ папки: без учёта регистра и завершающих «\» «/».</summary>
    public static string NormalizeKey(string path) => Key(path.Trim().TrimEnd('\\', '/'));

    private static bool IsPath(string s) => s.Contains(@":\") || s.StartsWith(@"\\") || s.StartsWith('/');

    private static (FolderAccess?, string?) Normalize(string s)
    {
        if (s.Length == 0) return (null, null);
        string n = Key(s);
        if (n is "full" or "f" || n.Contains("full")) return (FolderAccess.Full, s);
        if (n is "read" or "r" or "ro" || n.Contains("read")) return (FolderAccess.Read, s);
        return (FolderAccess.Other, s);
    }

    private static string Key(string s) => s.Trim().ToLowerInvariant();
}
