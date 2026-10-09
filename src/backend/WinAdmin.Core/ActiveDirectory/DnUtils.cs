using System.Text;

namespace WinAdmin.Core.ActiveDirectory;

/// <summary>Distinguished Name: разбор с учётом экранирования (\,), сравнение без учёта регистра.</summary>
public static class DnUtils
{
    public static IReadOnlyList<string> Split(string dn)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        for (int i = 0; i < dn.Length; i++)
        {
            char c = dn[i];
            if (c == '\\' && i + 1 < dn.Length)
            {
                current.Append(c).Append(dn[++i]);
                continue;
            }
            if (c == ',')
            {
                parts.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }
            current.Append(c);
        }
        if (current.ToString().Trim().Length > 0) parts.Add(current.ToString().Trim());
        return parts;
    }

    /// <summary>«ou=Проект , dc=x» → «OU=Проект,DC=x»: тип — в верхнем регистре, пробелы вокруг «=» и «,» убраны.</summary>
    public static string Normalize(string dn)
        => string.Join(",", Split(dn).Select(rdn =>
        {
            int eq = rdn.IndexOf('=');
            return eq < 0 ? rdn : rdn[..eq].Trim().ToUpperInvariant() + "=" + rdn[(eq + 1)..].Trim();
        }));

    public static bool IsUnderOrSame(string dn, string ancestor)
    {
        var a = Split(Normalize(dn));
        var b = Split(Normalize(ancestor));
        if (b.Count == 0 || b.Count > a.Count) return false;
        return a.Skip(a.Count - b.Count).SequenceEqual(b, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Имена OU от корня вниз (без корня): «CN=u,OU=Users,OU=Проект,{root}» → [Проект, Users].</summary>
    public static IReadOnlyList<string> RelativeOuPath(string dn, string root)
    {
        if (!IsUnderOrSame(dn, root)) return [];
        var parts = Split(Normalize(dn));
        int rootCount = Split(Normalize(root)).Count;
        return parts.Take(parts.Count - rootCount).Reverse()
            .Where(p => p.StartsWith("OU=", StringComparison.OrdinalIgnoreCase))
            .Select(p => Unescape(p[3..]))
            .ToList();
    }

    /// <summary>DN OU проекта (первого уровня под root), в котором лежит объект; null — вне проектов.</summary>
    public static string? ProjectDn(string dn, string root)
    {
        if (!IsUnderOrSame(dn, root)) return null;
        var parts = Split(Normalize(dn));
        int index = parts.Count - Split(Normalize(root)).Count - 1;
        if (index < 0 || !parts[index].StartsWith("OU=", StringComparison.OrdinalIgnoreCase)) return null;
        return string.Join(",", parts.Skip(index));
    }

    /// <summary>Значение первого RDN без экранирования: «CN=Иванов\, Пётр,…» → «Иванов, Пётр».</summary>
    public static string FirstValue(string dn)
    {
        var first = Split(dn).FirstOrDefault() ?? "";
        int eq = first.IndexOf('=');
        return Unescape(eq < 0 ? first : first[(eq + 1)..].Trim());
    }

    private static string Unescape(string value)
    {
        var sb = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length) i++;
            sb.Append(value[i]);
        }
        return sb.ToString();
    }
}
