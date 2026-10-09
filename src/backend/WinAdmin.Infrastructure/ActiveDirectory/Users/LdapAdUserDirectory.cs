using System.DirectoryServices.Protocols;
using System.Net;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.ActiveDirectory.Users;

namespace WinAdmin.Infrastructure.ActiveDirectory.Users;

/// <summary>Пользователи и группы AD (учётка компьютера; readCredential — только для тестов вне домена).</summary>
public sealed class LdapAdUserDirectory(
    IDirectorySettingsStore directory, IAdStructureStore structure, NetworkCredential? readCredential = null) : IAdUserDirectory
{
    private const string UserFilter = "(objectCategory=person)(objectClass=user)";
    private static readonly string[] Base =
        ["sAMAccountName", "distinguishedName", "objectSid", "displayName", "userAccountControl", "lastLogonTimestamp",
         "whenCreated", "primaryGroupID", "thumbnailPhoto"];

    public async Task<IReadOnlyList<AdUser>> ListUsersAsync(string baseDn, IReadOnlyCollection<string> attributes, CancellationToken ct = default)
    {
        var dir = await EnabledAsync(ct);
        return Run(dir, readCredential, c => LdapConnections
            .SearchPaged(c, baseDn, $"(&{UserFilter})", SearchScope.Subtree, [.. Base, .. attributes])
            .Select(e => ToUser(e, attributes)).OfType<AdUser>().ToList());
    }

    public async Task<AdUser?> FindUserAsync(string sam, IReadOnlyCollection<string> attributes, CancellationToken ct = default)
    {
        var dir = await EnabledAsync(ct);
        return Run(dir, readCredential, c =>
        {
            string baseDn = dir.BaseDn ?? LdapConnections.NamingContext(c);
            var response = LdapConnections.Search(c, new SearchRequest(baseDn,
                $"(&{UserFilter}(sAMAccountName={LdapFilter.Escape(sam)}))", SearchScope.Subtree, [.. Base, .. attributes]) { SizeLimit = 2 });
            return response.Entries.Count == 1 ? ToUser(response.Entries[0], attributes) : null;
        });
    }

    public async Task<IReadOnlyList<AdGroupRef>> GetGroupsAsync(AdUser user, CancellationToken ct = default)
    {
        var dir = await EnabledAsync(ct);
        return Run(dir, readCredential, c =>
        {
            var groups = new List<AdGroupRef>();
            var entry = ((SearchResponse)c.SendRequest(new SearchRequest(user.Dn, "(objectClass=*)", SearchScope.Base, "memberOf"))).Entries[0];
            foreach (string dn in entry.Attributes["memberOf"]?.GetValues(typeof(string)).Cast<string>() ?? [])
                groups.Add(new AdGroupRef(dn, DnUtils.FirstValue(dn), null, false));
            string primarySid = $"{LdapValues.DomainSid(user.Sid)}-{user.PrimaryGroupId}";
            if (FindBySid(c, dir, primarySid) is { } primary) groups.Insert(0, primary with { IsPrimary = true });
            return (IReadOnlyList<AdGroupRef>)groups.OrderByDescending(g => g.IsPrimary).ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        });
    }

    public async Task<byte[]?> GetPhotoAsync(string dn, CancellationToken ct = default)
    {
        var dir = await EnabledAsync(ct);
        return Run(dir, readCredential, c =>
            ((SearchResponse)c.SendRequest(new SearchRequest(dn, "(objectClass=*)", SearchScope.Base, "thumbnailPhoto")))
            .Entries[0].Attributes["thumbnailPhoto"]?[0] as byte[]);
    }

    public async Task<AdGroupRef?> FindGroupAsync(string nameOrDn, CancellationToken ct = default)
    {
        var dir = await EnabledAsync(ct);
        string value = nameOrDn.Trim();
        return Run(dir, readCredential, c =>
        {
            string baseDn = dir.BaseDn ?? LdapConnections.NamingContext(c);
            string filter = value.Contains('=')
                ? $"(&(objectCategory=group)(distinguishedName={LdapFilter.Escape(value)}))"
                : $"(&(objectCategory=group)(|(sAMAccountName={LdapFilter.Escape(value)})(cn={LdapFilter.Escape(value)})))";
            var response = LdapConnections.Search(c, new SearchRequest(baseDn, filter, SearchScope.Subtree, "objectSid", "cn") { SizeLimit = 2 });
            return response.Entries.Count == 1 ? ToGroup(response.Entries[0]) : null;
        });
    }

    public async Task<AdGroupRef> GetDomainUsersGroupAsync(CancellationToken ct = default)
    {
        var dir = await EnabledAsync(ct);
        return Run(dir, readCredential, c =>
        {
            string nc = LdapConnections.NamingContext(c);
            var domain = ((SearchResponse)c.SendRequest(new SearchRequest(nc, "(objectClass=*)", SearchScope.Base, "objectSid"))).Entries[0];
            string domainSid = LdapMapping.SidFromBytes((byte[])domain.Attributes["objectSid"][0]);
            return FindBySid(c, dir, domainSid + "-513")
                   ?? throw new InvalidOperationException("Группа «Пользователи домена» (RID 513) не найдена.");
        });
    }

    public async Task<string?> FindSampleUserAsync(string ouDn, CancellationToken ct = default)
    {
        var dir = await EnabledAsync(ct);
        return Run(dir, readCredential, c =>
        {
            var response = LdapConnections.Search(c, new SearchRequest(ouDn, $"(&{UserFilter})", SearchScope.Subtree, "distinguishedName") { SizeLimit = 1 });
            return response.Entries.Count > 0 ? response.Entries[0].DistinguishedName : null;
        });
    }

    public async Task<IReadOnlyList<string>> GetWriterSidsAsync(CancellationToken ct = default)
    {
        var dir = await EnabledAsync(ct);
        var credential = await structure.GetWriteCredentialAsync(ct);
        return Run(dir, readCredential, c =>
        {
            string baseDn = dir.BaseDn ?? LdapConnections.NamingContext(c);
            string filter = credential.Mode == AdWriteMode.ProcessAccount
                ? $"(&(objectCategory=computer)(sAMAccountName={LdapFilter.Escape(System.Environment.MachineName + "$")}))"
                : $"(&{UserFilter}(sAMAccountName={LdapFilter.Escape(DirectoryLogin.Parse(credential.Login ?? "").Sam)}))";
            var found = LdapConnections.Search(c, new SearchRequest(baseDn, filter, SearchScope.Subtree, "distinguishedName", "objectSid") { SizeLimit = 1 });
            var sids = new List<string> { "S-1-1-0", "S-1-5-11" };
            if (found.Entries.Count == 0) return (IReadOnlyList<string>)sids;
            var account = found.Entries[0];
            sids.Add(LdapMapping.SidFromBytes((byte[])account.Attributes["objectSid"][0]));
            var tokens = ((SearchResponse)c.SendRequest(new SearchRequest(account.DistinguishedName, "(objectClass=*)", SearchScope.Base, "tokenGroups"))).Entries[0];
            sids.AddRange(tokens.Attributes["tokenGroups"]?.GetValues(typeof(byte[])).Cast<byte[]>().Select(LdapMapping.SidFromBytes) ?? []);
            return sids.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        });
    }

    public async Task<byte[]?> ReadSecurityDescriptorAsync(string dn, CancellationToken ct = default)
    {
        var dir = await EnabledAsync(ct);
        return Run(dir, readCredential, c =>
        {
            var request = new SearchRequest(dn, "(objectClass=*)", SearchScope.Base, "nTSecurityDescriptor");
            request.Controls.Add(new SecurityDescriptorFlagControl(System.DirectoryServices.Protocols.SecurityMasks.Dacl));
            return ((SearchResponse)c.SendRequest(request)).Entries[0].Attributes["nTSecurityDescriptor"]?[0] as byte[];
        });
    }

    private static AdGroupRef? FindBySid(LdapConnection c, DirectorySettings dir, string sid)
    {
        string baseDn = dir.BaseDn ?? LdapConnections.NamingContext(c);
        var response = LdapConnections.Search(c, new SearchRequest(baseDn, $"(objectSid={LdapFilter.Sid(sid)})", SearchScope.Subtree, "objectSid", "cn") { SizeLimit = 1 });
        return response.Entries.Count == 1 ? ToGroup(response.Entries[0]) : null;
    }

    private static AdGroupRef ToGroup(SearchResultEntry e)
        => new(e.DistinguishedName, e.Attributes["cn"]?[0] as string ?? DnUtils.FirstValue(e.DistinguishedName),
            e.Attributes["objectSid"]?[0] is byte[] sid ? LdapMapping.SidFromBytes(sid) : null, false);

    private static AdUser? ToUser(SearchResultEntry e, IReadOnlyCollection<string> attributes)
    {
        if (e.Attributes["objectSid"]?[0] is not byte[] sid) return null;
        string? Str(string name) => e.Attributes[name]?[0] as string;
        var attrs = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in attributes) attrs[a] = Str(a);
        return new AdUser(
            Str("sAMAccountName") ?? "", e.DistinguishedName, LdapMapping.SidFromBytes(sid), Str("displayName"),
            LdapMapping.IsEnabled(Str("userAccountControl")), LdapValues.FileTime(Str("lastLogonTimestamp")),
            LdapValues.GeneralizedTime(Str("whenCreated")), e.Attributes["thumbnailPhoto"] is { Count: > 0 },
            int.TryParse(Str("primaryGroupID"), out int pg) ? pg : 513, attrs);
    }

    private async Task<DirectorySettings> EnabledAsync(CancellationToken ct)
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
}
