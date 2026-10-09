using System.Net;
using WinAdmin.Core.ActiveDirectory;

namespace WinAdmin.Infrastructure.ActiveDirectory;

public static class AdCredentials
{
    /// <summary>Служебная учётка → NetworkCredential; учётка службы → null (bind от имени процесса).</summary>
    public static NetworkCredential? ToNetwork(AdWriteCredential credential, string? domain)
    {
        if (credential.Mode == AdWriteMode.ProcessAccount) return null;
        if (string.IsNullOrWhiteSpace(credential.Login) || string.IsNullOrEmpty(credential.Password))
            throw new ArgumentException("Служебная учётка записи не задана: укажите логин и пароль (Настройки → Active Directory).");
        string login = credential.Login.Trim();
        int slash = login.IndexOf('\\');
        if (slash > 0) return new NetworkCredential(login[(slash + 1)..], credential.Password, login[..slash]);
        if (login.Contains('@')) return new NetworkCredential(login, credential.Password);
        return new NetworkCredential(login, credential.Password, domain);
    }
}
