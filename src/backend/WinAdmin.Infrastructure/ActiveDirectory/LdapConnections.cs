using System.DirectoryServices.Protocols;
using System.Net;
using WinAdmin.Core.ActiveDirectory;

namespace WinAdmin.Infrastructure.ActiveDirectory;

/// <summary>Общие операции LDAP: подключение с подписью/шифрованием или LDAPS, поиск с лимитом и постранично.</summary>
public static class LdapConnections
{
    public static LdapConnection Open(DirectorySettings s, NetworkCredential? credential)
    {
        int port = s.UseLdaps ? 636 : 389;
        var id = s.Server is null
            ? new LdapDirectoryIdentifier(s.Domain, port, fullyQualifiedDnsHostName: false, connectionless: false)
            : new LdapDirectoryIdentifier(s.Server, port);
        var connection = new LdapConnection(id) { AuthType = AuthType.Negotiate, Timeout = TimeSpan.FromSeconds(10) };
        connection.SessionOptions.ProtocolVersion = 3;
        connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
        if (s.UseLdaps)
            connection.SessionOptions.SecureSocketLayer = true;
        else
        {
            connection.SessionOptions.Signing = true;
            connection.SessionOptions.Sealing = true;
        }
        if (credential is not null) connection.Credential = credential;
        try
        {
            connection.Bind();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public static string NamingContext(LdapConnection connection)
    {
        var response = (SearchResponse)connection.SendRequest(
            new SearchRequest(null, "(objectClass=*)", SearchScope.Base, "defaultNamingContext"));
        return response.Entries[0].Attributes["defaultNamingContext"]?[0] as string
               ?? throw new DirectoryUnavailableException("Не удалось прочитать корень каталога");
    }

    /// <summary>Поиск с лимитом: «size limit exceeded» — не ошибка, берём частичный ответ.</summary>
    public static SearchResponse Search(LdapConnection connection, SearchRequest request)
    {
        try
        {
            return (SearchResponse)connection.SendRequest(request);
        }
        catch (DirectoryOperationException ex) when (ex.Response is SearchResponse { ResultCode: ResultCode.SizeLimitExceeded } partial)
        {
            return partial;
        }
    }

    /// <summary>Постраничный поиск (по 500): для списков больше лимита сервера (1000).</summary>
    public static IEnumerable<SearchResultEntry> SearchPaged(
        LdapConnection connection, string baseDn, string filter, SearchScope scope, params string[] attributes)
    {
        var page = new PageResultRequestControl(500);
        var request = new SearchRequest(baseDn, filter, scope, attributes);
        request.Controls.Add(page);
        while (true)
        {
            var response = (SearchResponse)connection.SendRequest(request);
            foreach (SearchResultEntry entry in response.Entries) yield return entry;
            var cookie = response.Controls.OfType<PageResultResponseControl>().FirstOrDefault()?.Cookie;
            if (cookie is null || cookie.Length == 0) yield break;
            page.Cookie = cookie;
        }
    }

    public static DirectoryUnavailableException Unavailable(Exception ex)
        => new("Контроллер домена недоступен", ex);
}
