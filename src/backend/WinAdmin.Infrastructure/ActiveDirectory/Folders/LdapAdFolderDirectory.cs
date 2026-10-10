using System.DirectoryServices.Protocols;
using System.Net;
using System.Text;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.ActiveDirectory.Folders;

namespace WinAdmin.Infrastructure.ActiveDirectory.Folders;

/// <summary>Группы sg_* и их участники: постранично, с диапазонным чтением member (> 1500).</summary>
public sealed class LdapAdFolderDirectory(IDirectorySettingsStore directory, NetworkCredential? readCredential = null) : IAdFolderDirectory
{
    private const int SecurityEnabled = unchecked((int)0x80000000);
    private static readonly string[] GroupAttributes = ["cn", "sAMAccountName", "objectSid", "description", "info", "groupType", "member"];
    private static readonly string[] MemberAttributes = ["objectClass", "sAMAccountName", "displayName", "cn", "userAccountControl"];

    public async Task<IReadOnlyList<AdFolderGroup>> ListGroupsAsync(string baseDn, string prefix, CancellationToken ct = default)
    {
        var dir = await EnabledAsync(ct);
        string filter = $"(&(objectCategory=group)(cn={LdapFilter.Escape(prefix)}*))";
        return Run(dir, c => LdapConnections.SearchPaged(c, baseDn, filter, SearchScope.Subtree, GroupAttributes)
            .Select(e => ToGroup(c, e)).ToList());
    }

    public async Task<IReadOnlyDictionary<string, AdMember>> ResolveMembersAsync(IEnumerable<string> dns, CancellationToken ct = default)
    {
        var dir = await EnabledAsync(ct);
        var all = dns.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return Run(dir, c =>
        {
            var result = new Dictionary<string, AdMember>(StringComparer.OrdinalIgnoreCase);
            string baseDn = dir.BaseDn ?? LdapConnections.NamingContext(c);
            foreach (var batch in all.Chunk(30))
            {
                var filter = new StringBuilder("(|");
                foreach (var dn in batch) filter.Append("(distinguishedName=").Append(LdapFilter.Escape(dn)).Append(')');
                filter.Append(')');
                foreach (var e in LdapConnections.SearchPaged(c, baseDn, filter.ToString(), SearchScope.Subtree, MemberAttributes))
                    result[e.DistinguishedName] = ToMember(e);
            }
            return (IReadOnlyDictionary<string, AdMember>)result;
        });
    }

    public async Task<AdFolderGroup?> GetGroupAsync(string dn, CancellationToken ct = default)
    {
        var dir = await EnabledAsync(ct);
        return Run(dir, c =>
        {
            try
            {
                var e = ((SearchResponse)c.SendRequest(new SearchRequest(dn, "(objectCategory=group)", SearchScope.Base, GroupAttributes))).Entries;
                return e.Count == 1 ? ToGroup(c, e[0]) : null;
            }
            catch (DirectoryOperationException ex) when (ex.Response?.ResultCode == ResultCode.NoSuchObject)
            {
                return null;
            }
        });
    }

    public async Task<AdFolderGroup?> FindGroupByNameAsync(string sam, CancellationToken ct = default)
    {
        var dir = await EnabledAsync(ct);
        return Run(dir, c =>
        {
            string baseDn = dir.BaseDn ?? LdapConnections.NamingContext(c);
            var r = LdapConnections.Search(c, new SearchRequest(baseDn,
                $"(&(objectCategory=group)(sAMAccountName={LdapFilter.Escape(sam)}))", SearchScope.Subtree, GroupAttributes) { SizeLimit = 2 });
            return r.Entries.Count == 1 ? ToGroup(c, r.Entries[0]) : null;
        });
    }

    public async Task<AdMember?> FindMemberAsync(string samOrDn, CancellationToken ct = default)
    {
        var dir = await EnabledAsync(ct);
        string value = samOrDn.Trim();
        return Run(dir, c =>
        {
            string baseDn = dir.BaseDn ?? LdapConnections.NamingContext(c);
            string filter = value.Contains('=')
                ? $"(distinguishedName={LdapFilter.Escape(value)})"
                : $"(&(|(objectCategory=person)(objectCategory=group))(sAMAccountName={LdapFilter.Escape(value)}))";
            var r = LdapConnections.Search(c, new SearchRequest(baseDn, filter, SearchScope.Subtree, MemberAttributes) { SizeLimit = 2 });
            return r.Entries.Count == 1 ? ToMember(r.Entries[0]) : null;
        });
    }

    private static AdFolderGroup ToGroup(LdapConnection c, SearchResultEntry e)
    {
        string? Str(string name) => e.Attributes[name]?[0] as string;
        int.TryParse(Str("groupType"), out int groupType);
        return new AdFolderGroup(
            e.DistinguishedName, Str("cn") ?? DnUtils.FirstValue(e.DistinguishedName),
            e.Attributes["objectSid"]?[0] is byte[] sid ? LdapMapping.SidFromBytes(sid) : null,
            Str("description"), Str("info"), (groupType & SecurityEnabled) != 0, Members(c, e));
    }

    /// <summary>member целиком; у больших групп AD отдаёт member;range=0-1499 — дочитываем кусками.</summary>
    private static IReadOnlyList<string> Members(LdapConnection c, SearchResultEntry e)
    {
        var result = new List<string>();
        string? rangedName = null;
        foreach (string name in e.Attributes.AttributeNames)
        {
            if (name.Equals("member", StringComparison.OrdinalIgnoreCase))
                result.AddRange(e.Attributes[name].GetValues(typeof(string)).Cast<string>());
            else if (name.StartsWith("member;range=", StringComparison.OrdinalIgnoreCase))
            {
                result.AddRange(e.Attributes[name].GetValues(typeof(string)).Cast<string>());
                rangedName = name;
            }
        }
        while (rangedName is not null && LdapRange.Next(rangedName) is { Done: false } next)
        {
            var part = ((SearchResponse)c.SendRequest(new SearchRequest(e.DistinguishedName, "(objectClass=*)", SearchScope.Base,
                $"member;range={next.Start}-*"))).Entries[0];
            rangedName = part.Attributes.AttributeNames.Cast<string>()
                .FirstOrDefault(n => n.StartsWith("member;range=", StringComparison.OrdinalIgnoreCase));
            if (rangedName is null) break;
            result.AddRange(part.Attributes[rangedName].GetValues(typeof(string)).Cast<string>());
        }
        return result;
    }

    private static AdMember ToMember(SearchResultEntry e)
    {
        string? Str(string name) => e.Attributes[name]?[0] as string;
        bool isGroup = e.Attributes["objectClass"]?.GetValues(typeof(string)).Cast<string>()
            .Contains("group", StringComparer.OrdinalIgnoreCase) == true;
        return new AdMember(e.DistinguishedName, Str("displayName") ?? Str("cn") ?? DnUtils.FirstValue(e.DistinguishedName),
            Str("sAMAccountName"), isGroup, isGroup || LdapMapping.IsEnabled(Str("userAccountControl")));
    }

    private async Task<DirectorySettings> EnabledAsync(CancellationToken ct)
    {
        var dir = await directory.GetAsync(ct);
        if (!dir.Enabled || dir.Domain is null) throw new DirectoryUnavailableException("Подключение к домену выключено");
        return dir;
    }

    private T Run<T>(DirectorySettings dir, Func<LdapConnection, T> action)
    {
        try
        {
            using var connection = LdapConnections.Open(dir, readCredential);
            return action(connection);
        }
        catch (LdapException ex)
        {
            throw LdapConnections.Unavailable(ex);
        }
    }
}
