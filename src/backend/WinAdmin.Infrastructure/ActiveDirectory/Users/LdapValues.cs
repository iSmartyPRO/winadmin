using System.Globalization;

namespace WinAdmin.Infrastructure.ActiveDirectory.Users;

public static class LdapValues
{
    /// <summary>FILETIME (lastLogonTimestamp и т.п.); 0 и «никогда» → null.</summary>
    public static DateTimeOffset? FileTime(string? value)
        => long.TryParse(value, out long ft) && ft > 0 && ft < DateTime.MaxValue.ToFileTimeUtc()
            ? DateTimeOffset.FromFileTime(ft).ToUniversalTime() : null;

    /// <summary>GeneralizedTime «20261009123005.0Z» → UTC.</summary>
    public static DateTimeOffset? GeneralizedTime(string? value)
        => value is { Length: >= 14 } && DateTime.TryParseExact(value[..14], "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt)
            ? new DateTimeOffset(dt, TimeSpan.Zero) : null;

    public static string DomainSid(string userSid) => userSid[..userSid.LastIndexOf('-')];
}
