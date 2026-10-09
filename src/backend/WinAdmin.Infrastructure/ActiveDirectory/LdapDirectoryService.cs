using System.DirectoryServices.Protocols;
using System.Net;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;

namespace WinAdmin.Infrastructure.ActiveDirectory;

/// <summary>
/// AD через LDAP: Negotiate с подписью и шифрованием (389) или LDAPS (636).
/// readCredential == null — учётка процесса (служба SYSTEM → учётка компьютера); задаётся только в тестах.
/// </summary>
public sealed class LdapDirectoryService(IDirectorySettingsStore settingsStore, NetworkCredential? readCredential = null) : IDirectoryService
{
    private static readonly string[] Attributes =
        ["objectSid", "objectClass", "sAMAccountName", "displayName", "userPrincipalName", "distinguishedName", "userAccountControl"];
    private const string UserFilter = "(objectCategory=person)(objectClass=user)";
    private const string GroupFilter = "(objectCategory=group)";

    public Task<DirectoryObject?> FindUserAsync(string login, CancellationToken ct = default)
    {
        var (sam, upn) = DirectoryLogin.Parse(login);
        string byName = upn is null
            ? $"(sAMAccountName={LdapFilter.Escape(sam)})"
            : $"(|(userPrincipalName={LdapFilter.Escape(upn)})(sAMAccountName={LdapFilter.Escape(sam)}))";
        return FindOneAsync($"(&{UserFilter}{byName})", ct);
    }

    public Task<DirectoryObject?> FindBySidAsync(string sid, CancellationToken ct = default)
        => FindOneAsync($"(objectSid={LdapFilter.Sid(sid)})", ct);

    public async Task<IReadOnlyList<string>> GetTokenGroupsAsync(string userSid, CancellationToken ct = default)
    {
        var user = await FindBySidAsync(userSid, ct);
        if (user?.DistinguishedName is null) return [];
        return await RunAsync((connection, _) =>
        {
            var response = (SearchResponse)connection.SendRequest(
                new SearchRequest(user.DistinguishedName, "(objectClass=*)", SearchScope.Base, "tokenGroups"));
            var entry = response.Entries.Cast<SearchResultEntry>().FirstOrDefault();
            if (entry?.Attributes["tokenGroups"] is not { } attr) return (IReadOnlyList<string>)[];
            return attr.GetValues(typeof(byte[])).Cast<byte[]>().Select(LdapMapping.SidFromBytes).ToList();
        }, ct);
    }

    public async Task<IReadOnlyList<DirectoryObject>> SearchAsync(string query, DirectoryObjectKind? kind, int limit, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        string kindFilter = kind switch
        {
            DirectoryObjectKind.User => $"(&{UserFilter})",
            DirectoryObjectKind.Group => GroupFilter,
            _ => $"(|(&{UserFilter}){GroupFilter})",
        };
        string filter = $"(&(anr={LdapFilter.Escape(query.Trim())}){kindFilter})";
        return await RunAsync((connection, baseDn) =>
        {
            var response = Search(connection,
                new SearchRequest(baseDn, filter, SearchScope.Subtree, Attributes) { SizeLimit = Math.Clamp(limit, 1, 100) });
            return (IReadOnlyList<DirectoryObject>)response.Entries.Cast<SearchResultEntry>().Select(ToObject).OfType<DirectoryObject>().ToList();
        }, ct);
    }

    public async Task<bool> ValidateCredentialsAsync(string login, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(password)) return false;
        var settings = await EnabledSettingsAsync(ct);
        var (sam, upn) = DirectoryLogin.Parse(login);
        var credential = upn is not null ? new NetworkCredential(upn, password) : new NetworkCredential(sam, password, settings.Domain);
        try
        {
            using var connection = Connect(settings, credential);
            return true;
        }
        catch (LdapException ex) when (ex.ErrorCode == 49) // invalid credentials
        {
            return false;
        }
        catch (LdapException ex)
        {
            throw new DirectoryUnavailableException("Контроллер домена недоступен", ex);
        }
    }

    public async Task<IReadOnlyList<DirectoryTestStep>> TestConnectionAsync(CancellationToken ct = default)
    {
        var steps = new List<DirectoryTestStep>();
        var settings = await settingsStore.GetAsync(ct);
        if (!settings.Enabled || settings.Domain is null)
        {
            steps.Add(new("Настройки", false, "Подключение к домену выключено или домен не указан"));
            return steps;
        }
        steps.Add(new("Настройки", true, $"Домен {settings.Domain}, сервер {settings.Server ?? "(поиск через DNS)"}, {(settings.UseLdaps ? "LDAPS 636" : "LDAP 389 с подписью")}"));
        LdapConnection connection;
        try
        {
            connection = Connect(settings, readCredential);
            steps.Add(new("Подключение", true, "Вход учёткой компьютера выполнен"));
        }
        catch (LdapException ex)
        {
            steps.Add(new("Подключение", false, ex.Message));
            return steps;
        }
        using (connection)
        {
            try
            {
                string baseDn = settings.BaseDn ?? ReadNamingContext(connection);
                steps.Add(new("Корень каталога", true, baseDn));
                var response = Search(connection,
                    new SearchRequest(baseDn, $"(&{UserFilter})", SearchScope.Subtree, "sAMAccountName") { SizeLimit = 1 });
                steps.Add(new("Поиск", true, response.Entries.Count > 0 ? "Пользователи находятся" : "Пользователи не найдены"));
            }
            catch (Exception ex) when (ex is LdapException or DirectoryOperationException)
            {
                steps.Add(new("Поиск", false, ex.Message));
            }
        }
        return steps;
    }

    private Task<DirectoryObject?> FindOneAsync(string filter, CancellationToken ct)
        => RunAsync((connection, baseDn) =>
        {
            var response = Search(connection, new SearchRequest(baseDn, filter, SearchScope.Subtree, Attributes) { SizeLimit = 2 });
            return response.Entries.Count == 1 ? ToObject(response.Entries[0]) : null;
        }, ct);

    /// <summary>Поиск с лимитом: AD отвечает «size limit exceeded», если записей больше, — это не ошибка, берём частичный ответ.</summary>
    private static SearchResponse Search(LdapConnection connection, SearchRequest request)
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

    private async Task<T> RunAsync<T>(Func<LdapConnection, string, T> action, CancellationToken ct)
    {
        var settings = await EnabledSettingsAsync(ct);
        try
        {
            using var connection = Connect(settings, readCredential);
            return action(connection, settings.BaseDn ?? ReadNamingContext(connection));
        }
        catch (LdapException ex)
        {
            throw new DirectoryUnavailableException("Контроллер домена недоступен", ex);
        }
    }

    private async Task<DirectorySettings> EnabledSettingsAsync(CancellationToken ct)
    {
        var settings = await settingsStore.GetAsync(ct);
        if (!settings.Enabled || settings.Domain is null)
            throw new DirectoryUnavailableException("Подключение к домену выключено");
        return settings;
    }

    private static LdapConnection Connect(DirectorySettings s, NetworkCredential? credential)
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

    private static string ReadNamingContext(LdapConnection connection)
    {
        var response = (SearchResponse)connection.SendRequest(
            new SearchRequest(null, "(objectClass=*)", SearchScope.Base, "defaultNamingContext"));
        return response.Entries[0].Attributes["defaultNamingContext"]?[0] as string
               ?? throw new DirectoryUnavailableException("Не удалось прочитать корень каталога");
    }

    private static DirectoryObject? ToObject(SearchResultEntry entry)
    {
        if (entry.Attributes["objectSid"]?[0] is not byte[] sid) return null;
        string? Str(string name) => entry.Attributes[name]?[0] as string;
        bool isGroup = entry.Attributes["objectClass"]?.GetValues(typeof(string)).Cast<string>()
            .Contains("group", StringComparer.OrdinalIgnoreCase) == true;
        return new DirectoryObject(
            LdapMapping.SidFromBytes(sid),
            isGroup ? DirectoryObjectKind.Group : DirectoryObjectKind.User,
            Str("sAMAccountName") ?? "",
            Str("displayName"),
            Str("userPrincipalName"),
            Str("distinguishedName") ?? entry.DistinguishedName,
            isGroup || LdapMapping.IsEnabled(Str("userAccountControl")));
    }
}
