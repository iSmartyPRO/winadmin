namespace WinAdmin.Core.ActiveDirectory;

public static class DirectoryLogin
{
    /// <summary>«DOMAIN\user» → (user, null); «user@domain» → (user, user@domain); «user» → (user, null).</summary>
    public static (string Sam, string? Upn) Parse(string login)
    {
        string value = (login ?? "").Trim();
        int slash = value.IndexOf('\\');
        if (slash >= 0) value = value[(slash + 1)..];
        int at = value.IndexOf('@');
        string sam = at >= 0 ? value[..at] : value;
        if (sam.Length == 0 || (at >= 0 && at == value.Length - 1))
            throw new ArgumentException("Укажите логин.");
        return (sam, at >= 0 ? value : null);
    }
}
