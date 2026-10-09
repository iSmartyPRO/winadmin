using System.Security.Principal;

namespace WinAdmin.Infrastructure.ActiveDirectory;

public static class LdapMapping
{
    private const int AccountDisable = 0x2;

    public static string SidFromBytes(byte[] bytes) => new SecurityIdentifier(bytes, 0).Value;

    public static bool IsEnabled(string? userAccountControl)
        => !int.TryParse(userAccountControl, out int uac) || (uac & AccountDisable) == 0;
}
