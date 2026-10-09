using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.Security;

namespace WinAdmin.Infrastructure.ActiveDirectory;

/// <summary>Пароль → bind → SID → группы → права. Нет ни одного права — NoAccess.</summary>
public sealed class DirectorySignInService(
    IDirectoryService directory, IDirectorySettingsStore settings, IAdGroupCache groups, IAccessService access) : IDirectorySignIn
{
    public async Task<DirectorySignInResult> PasswordAsync(string login, string password, CancellationToken ct = default)
    {
        if (!(await settings.GetAsync(ct)).Enabled) return new(DirectorySignInStatus.NotConfigured);
        if (string.IsNullOrWhiteSpace(password)) return new(DirectorySignInStatus.InvalidCredentials);
        try
        {
            if (!await directory.ValidateCredentialsAsync(login, password, ct))
                return new(DirectorySignInStatus.InvalidCredentials);
            var account = await directory.FindUserAsync(login, ct);
            return account is null
                ? new(DirectorySignInStatus.InvalidCredentials)
                : await CompleteAsync(account.Sid, ct);
        }
        catch (ArgumentException)
        {
            return new(DirectorySignInStatus.InvalidCredentials);
        }
        catch (DirectoryUnavailableException)
        {
            return new(DirectorySignInStatus.Unavailable);
        }
    }

    public async Task<DirectorySignInResult> CompleteAsync(string userSid, CancellationToken ct = default)
    {
        if (!(await settings.GetAsync(ct)).Enabled) return new(DirectorySignInStatus.NotConfigured);
        DirectoryObject? account;
        try { account = await directory.FindBySidAsync(userSid, ct); }
        catch (DirectoryUnavailableException) { return new(DirectorySignInStatus.Unavailable); }
        if (account is not { Kind: DirectoryObjectKind.User }) return new(DirectorySignInStatus.InvalidCredentials);
        if (!account.Enabled) return new(DirectorySignInStatus.Disabled, account);

        var groupSids = await groups.GetGroupsAsync(userSid, ct);
        if (groupSids is null) return new(DirectorySignInStatus.Unavailable, account);
        var permissions = await access.GetAsync(new PrincipalRef(PrincipalType.AdUser, userSid, groupSids), ct);
        return permissions.Map.Count == 0
            ? new(DirectorySignInStatus.NoAccess, account)
            : new(DirectorySignInStatus.Ok, account);
    }
}
