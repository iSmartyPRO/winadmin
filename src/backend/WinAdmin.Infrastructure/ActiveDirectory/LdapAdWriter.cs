using System.DirectoryServices.Protocols;
using Microsoft.Extensions.Logging;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;

namespace WinAdmin.Infrastructure.ActiveDirectory;

/// <summary>Запись в AD учёткой записи по зашифрованному каналу. Детали ошибок — в лог, оператору — понятный текст.</summary>
public sealed class LdapAdWriter(IDirectorySettingsStore directory, IAdStructureStore structure, ILogger<LdapAdWriter> logger) : IAdWriter
{
    public Task ModifyAttributesAsync(string dn, IReadOnlyDictionary<string, string?> changes, CancellationToken ct = default)
        => RunAsync("изменение атрибутов", dn, c =>
        {
            Send(c, AdWriteRequests.BuildModify(dn, changes));
            return true;
        }, ct);

    public Task<bool> AddMemberAsync(string groupDn, string memberDn, CancellationToken ct = default)
        => RunAsync("изменение членства", groupDn, c =>
        {
            var mod = new DirectoryAttributeModification { Name = "member", Operation = DirectoryAttributeOperation.Add };
            mod.Add(memberDn);
            return Send(c, new ModifyRequest(groupDn, mod), tolerate: [ResultCode.EntryAlreadyExists, ResultCode.AttributeOrValueExists]);
        }, ct);

    public Task<bool> RemoveMemberAsync(string groupDn, string memberDn, CancellationToken ct = default)
        => RunAsync("изменение членства", groupDn, c =>
        {
            var mod = new DirectoryAttributeModification { Name = "member", Operation = DirectoryAttributeOperation.Delete };
            mod.Add(memberDn);
            return Send(c, new ModifyRequest(groupDn, mod), tolerate: [ResultCode.NoSuchAttribute, ResultCode.UnwillingToPerform]);
        }, ct);

    public Task SetPrimaryGroupAsync(string userDn, string groupSid, CancellationToken ct = default)
        => RunAsync("смену основной группы", userDn, c =>
        {
            Send(c, new ModifyRequest(userDn, DirectoryAttributeOperation.Replace, "primaryGroupID", AdWriteRequests.Rid(groupSid)));
            return true;
        }, ct);

    public Task SetEnabledAsync(string userDn, bool enabled, CancellationToken ct = default)
        => RunAsync(enabled ? "включение учётки" : "отключение учётки", userDn, c =>
        {
            var read = (SearchResponse)c.SendRequest(new SearchRequest(userDn, "(objectClass=*)", SearchScope.Base, "userAccountControl"));
            int uac = int.TryParse(read.Entries[0].Attributes["userAccountControl"]?[0] as string, out int v) ? v : 512;
            Send(c, new ModifyRequest(userDn, DirectoryAttributeOperation.Replace, "userAccountControl",
                AdWriteRequests.ToggleDisabled(uac, enabled).ToString()));
            return true;
        }, ct);

    public Task ResetPasswordAsync(string userDn, string password, bool mustChange, CancellationToken ct = default)
        => RunAsync("сброс пароля", userDn, c =>
        {
            var request = new ModifyRequest(userDn);
            var pwd = new DirectoryAttributeModification { Name = "unicodePwd", Operation = DirectoryAttributeOperation.Replace };
            pwd.Add(AdWriteRequests.PasswordValue(password));
            request.Modifications.Add(pwd);
            if (mustChange) request.Modifications.Add(Replace("pwdLastSet", "0"));
            Send(c, request);
            return true;
        }, ct);

    public Task<string> MoveAsync(string dn, string targetOuDn, CancellationToken ct = default)
        => RunAsync("перенос", dn, c =>
        {
            string rdn = AdWriteRequests.Rdn(dn);
            Send(c, new ModifyDNRequest(dn, targetOuDn, rdn) { DeleteOldRdn = true });
            return rdn + "," + targetOuDn;
        }, ct);

    public Task<string> CreateGroupAsync(string ouDn, string cn, string description, CancellationToken ct = default)
        => RunAsync("создание группы", ouDn, c =>
        {
            string dn = $"CN={EscapeRdn(cn)},{ouDn}";
            var request = new AddRequest(dn,
                new DirectoryAttribute("objectClass", "group"),
                new DirectoryAttribute("sAMAccountName", cn),
                new DirectoryAttribute("groupType", AdWriteRequests.GlobalSecurityGroupType),
                new DirectoryAttribute("description", description));
            Send(c, request);
            return dn;
        }, ct);

    public Task SetPhotoAsync(string userDn, byte[]? photo, CancellationToken ct = default)
        => RunAsync(photo is null ? "удаление фото" : "загрузку фото", userDn, c =>
        {
            // Replace без значений удаляет фото и не ошибается, если его не было.
            var mod = new DirectoryAttributeModification { Name = "thumbnailPhoto", Operation = DirectoryAttributeOperation.Replace };
            if (photo is not null) mod.Add(photo);
            Send(c, new ModifyRequest(userDn, mod));
            return true;
        }, ct);

    private async Task<T> RunAsync<T>(string operation, string target, Func<LdapConnection, T> action, CancellationToken ct)
    {
        var dir = await directory.GetAsync(ct);
        if (!dir.Enabled || dir.Domain is null) throw new DirectoryUnavailableException("Подключение к домену выключено");
        var credential = await structure.GetWriteCredentialAsync(ct);
        var network = AdCredentials.ToNetwork(credential, dir.Domain);
        try
        {
            using var connection = LdapConnections.Open(dir, network);
            if (!dir.UseLdaps && !connection.SessionOptions.Sealing)
                throw new AdWriteException(0, "Канал к контроллеру домена не зашифрован — запись запрещена. См. «Проверка окружения».");
            return action(connection);
        }
        catch (DirectoryOperationException ex) when (ex.Response is { } response)
        {
            logger.LogWarning("AD: {Operation} для {Target} учёткой {Account}: {Code} {Message}",
                operation, target, credential, response.ResultCode, response.ErrorMessage);
            throw AdWriteRequests.Translate(response.ResultCode, credential.ToString(), operation, target, response.ErrorMessage);
        }
        catch (LdapException ex) when (ex.ErrorCode == 49)
        {
            throw new AdWriteException(49, $"Учётка записи {credential} не может войти (неверный пароль, отключена или заблокирована).");
        }
        catch (LdapException ex)
        {
            logger.LogWarning(ex, "AD: {Operation} для {Target}: контроллер недоступен", operation, target);
            throw LdapConnections.Unavailable(ex);
        }
    }

    /// <summary>true — изменение выполнено; false — допустимый «уже так» (tolerate).</summary>
    private static bool Send(LdapConnection connection, DirectoryRequest request, ResultCode[]? tolerate = null)
    {
        try
        {
            connection.SendRequest(request);
            return true;
        }
        catch (DirectoryOperationException ex) when (ex.Response is { } r && tolerate?.Contains(r.ResultCode) == true)
        {
            return false;
        }
    }

    private static DirectoryAttributeModification Replace(string name, string value)
    {
        var mod = new DirectoryAttributeModification { Name = name, Operation = DirectoryAttributeOperation.Replace };
        mod.Add(value);
        return mod;
    }

    private static string EscapeRdn(string value)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < value.Length; i++)
        {
            char ch = value[i];
            if (ch is ',' or '+' or '"' or '\\' or '<' or '>' or ';' or '=' || (i == 0 && ch is '#' or ' ') || (i == value.Length - 1 && ch == ' '))
                sb.Append('\\');
            sb.Append(ch);
        }
        return sb.ToString();
    }
}
