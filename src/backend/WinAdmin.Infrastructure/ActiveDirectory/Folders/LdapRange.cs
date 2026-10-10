namespace WinAdmin.Infrastructure.ActiveDirectory.Folders;

/// <summary>Диапазонное чтение многозначных атрибутов (member;range=0-1499).</summary>
public static class LdapRange
{
    /// <summary>null — атрибут не диапазонный; Done — последний кусок; иначе Start следующего.</summary>
    public static (int Start, bool Done)? Next(string attributeName)
    {
        int i = attributeName.IndexOf(";range=", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;
        string range = attributeName[(i + 7)..];
        int dash = range.IndexOf('-');
        string end = range[(dash + 1)..];
        return end == "*" ? (-1, true) : (int.Parse(end) + 1, false);
    }
}
