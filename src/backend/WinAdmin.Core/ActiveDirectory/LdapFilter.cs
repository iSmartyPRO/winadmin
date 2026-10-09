using System.Security.Principal;
using System.Text;

namespace WinAdmin.Core.ActiveDirectory;

/// <summary>Значения для LDAP-фильтров (RFC 4515).</summary>
public static class LdapFilter
{
    public static string Escape(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            sb.Append(c switch
            {
                '*' => "\\2a",
                '(' => "\\28",
                ')' => "\\29",
                '\\' => "\\5c",
                '\0' => "\\00",
                _ => c.ToString(),
            });
        }
        return sb.ToString();
    }

    /// <summary>SID в виде экранированных байтов для (objectSid=…).</summary>
    public static string Sid(string sid)
    {
        SecurityIdentifier parsed;
        try { parsed = new SecurityIdentifier(sid); }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        { throw new ArgumentException("Некорректный SID.", nameof(sid), ex); }
        var bytes = new byte[parsed.BinaryLength];
        parsed.GetBinaryForm(bytes, 0);
        return string.Concat(bytes.Select(b => $"\\{b:x2}"));
    }
}
