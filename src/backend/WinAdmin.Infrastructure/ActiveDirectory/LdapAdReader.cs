using System.DirectoryServices.Protocols;
using System.Net;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;

namespace WinAdmin.Infrastructure.ActiveDirectory;

/// <summary>Чтение AD для модулей: учётка компьютера (readCredential — только для тестов вне домена).</summary>
public sealed class LdapAdReader(
    IDirectorySettingsStore directory, IAdStructureStore structure, NetworkCredential? readCredential = null) : IAdReader
{
    public async Task<IReadOnlyList<AdProject>> ListProjectsAsync(bool includeHidden = false, CancellationToken ct = default)
    {
        var (dir, st) = await SettingsAsync(ct);
        return Run(dir, readCredential, connection =>
        {
            var projects = LdapConnections.SearchPaged(connection, st.RootOu!, "(objectClass=organizationalUnit)", SearchScope.OneLevel, "ou")
                .Select(e => new AdProject(DnUtils.Normalize(e.DistinguishedName), DnUtils.FirstValue(e.DistinguishedName)))
                .Where(p => includeHidden || !st.HiddenOus.Contains(p.Name, StringComparer.OrdinalIgnoreCase))
                .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            return (IReadOnlyList<AdProject>)projects;
        });
    }

    public async Task<bool> ExistsAsync(string dn, CancellationToken ct = default)
    {
        var dir = await EnabledDirectoryAsync(ct);
        return Run(dir, readCredential, connection =>
        {
            try
            {
                connection.SendRequest(new SearchRequest(dn, "(objectClass=*)", SearchScope.Base, "distinguishedName"));
                return true;
            }
            catch (DirectoryOperationException ex) when (ex.Response?.ResultCode == ResultCode.NoSuchObject)
            {
                return false;
            }
        });
    }

    public async Task<EffectiveRights> ReadEffectiveAsync(string dn, CancellationToken ct = default)
    {
        var dir = await EnabledDirectoryAsync(ct);
        var credential = AdCredentials.ToNetwork(await structure.GetWriteCredentialAsync(ct), dir.Domain);
        return Run(dir, credential, connection =>
        {
            var response = (SearchResponse)connection.SendRequest(new SearchRequest(dn, "(objectClass=*)", SearchScope.Base,
                "allowedAttributesEffective", "allowedChildClassesEffective"));
            var entry = response.Entries[0];
            return new EffectiveRights(Values(entry, "allowedAttributesEffective"), Values(entry, "allowedChildClassesEffective"));
        });
    }

    public async Task<WriterStatus> GetWriterStatusAsync(CancellationToken ct = default)
    {
        DirectorySettings dir;
        AdWriteCredential credential;
        try
        {
            dir = await EnabledDirectoryAsync(ct);
            credential = await structure.GetWriteCredentialAsync(ct);
        }
        catch (Exception ex) when (ex is DirectoryUnavailableException or SecretUnavailableException)
        {
            return new WriterStatus("?", false, ex is SecretUnavailableException
                ? "Пароль учётки записи не расшифровывается (ключ шифрования изменён или повреждён) — задайте его заново"
                : ex.Message, null, null, null, false);
        }

        string account = credential.Mode == AdWriteMode.ProcessAccount
            ? $"учётка службы ({System.Environment.MachineName}$)"
            : credential.Login ?? "(не задана)";
        NetworkCredential? network;
        try { network = AdCredentials.ToNetwork(credential, dir.Domain); }
        catch (ArgumentException ex) { return new WriterStatus(account, false, ex.Message, null, null, null, false); }

        try
        {
            using var connection = LdapConnections.Open(dir, network);
            bool encrypted = dir.UseLdaps || connection.SessionOptions.Sealing;
            if (credential.Mode == AdWriteMode.ProcessAccount)
                return new WriterStatus(account, true, null, null, null, null, encrypted);

            var (sam, upn) = DirectoryLogin.Parse(credential.Login!);
            string baseDn = dir.BaseDn ?? LdapConnections.NamingContext(connection);
            string filter = upn is null
                ? $"(&(objectCategory=person)(objectClass=user)(sAMAccountName={LdapFilter.Escape(sam)}))"
                : $"(&(objectCategory=person)(objectClass=user)(userPrincipalName={LdapFilter.Escape(upn)}))";
            var response = LdapConnections.Search(connection, new SearchRequest(baseDn, filter, SearchScope.Subtree,
                "userAccountControl", "lockoutTime", "msDS-UserPasswordExpiryTimeComputed") { SizeLimit = 1 });
            if (response.Entries.Count == 0)
                return new WriterStatus(account, true, "Учётка найдена при входе, но не найдена поиском", null, null, null, encrypted);
            var e = response.Entries[0];
            bool enabled = LdapMapping.IsEnabled(e.Attributes["userAccountControl"]?[0] as string);
            bool locked = long.TryParse(e.Attributes["lockoutTime"]?[0] as string, out long lockout) && lockout > 0;
            DateTimeOffset? expires = long.TryParse(e.Attributes["msDS-UserPasswordExpiryTimeComputed"]?[0] as string, out long ft)
                                      && ft > 0 && ft < DateTime.MaxValue.ToFileTimeUtc()
                ? DateTimeOffset.FromFileTime(ft) : null;
            return new WriterStatus(account, true, null, enabled, locked, expires, encrypted);
        }
        catch (LdapException ex) when (ex.ErrorCode == 49)
        {
            return new WriterStatus(account, false, "Неверный логин или пароль учётки записи (или учётка отключена/заблокирована)", null, null, null, false);
        }
        catch (Exception ex) when (ex is LdapException or DirectoryOperationException)
        {
            return new WriterStatus(account, false, "Контроллер домена недоступен: " + ex.Message, null, null, null, false);
        }
    }

    private async Task<(DirectorySettings, AdStructureSettings)> SettingsAsync(CancellationToken ct)
    {
        var dir = await EnabledDirectoryAsync(ct);
        var st = await structure.GetAsync(ct);
        if (st.RootOu is null) throw new InvalidOperationException("Корневая OU не задана (Настройки → Active Directory).");
        return (dir, st);
    }

    private async Task<DirectorySettings> EnabledDirectoryAsync(CancellationToken ct)
    {
        var dir = await directory.GetAsync(ct);
        if (!dir.Enabled || dir.Domain is null) throw new DirectoryUnavailableException("Подключение к домену выключено");
        return dir;
    }

    private static T Run<T>(DirectorySettings dir, NetworkCredential? credential, Func<LdapConnection, T> action)
    {
        try
        {
            using var connection = LdapConnections.Open(dir, credential);
            return action(connection);
        }
        catch (LdapException ex)
        {
            throw LdapConnections.Unavailable(ex);
        }
    }

    private static IReadOnlySet<string> Values(SearchResultEntry entry, string attribute)
        => entry.Attributes[attribute]?.GetValues(typeof(string)).Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase)
           ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
