using WinAdmin.Core.ActiveDirectory;

namespace WinAdmin.Core.Abstractions;

/// <summary>Каталог AD (учётка компьютера). Ошибки сети — DirectoryUnavailableException.</summary>
public interface IDirectoryService
{
    Task<DirectoryObject?> FindUserAsync(string login, CancellationToken ct = default);
    Task<DirectoryObject?> FindBySidAsync(string sid, CancellationToken ct = default);
    /// <summary>SID всех групп пользователя (транзитивно, tokenGroups).</summary>
    Task<IReadOnlyList<string>> GetTokenGroupsAsync(string userSid, CancellationToken ct = default);
    Task<IReadOnlyList<DirectoryObject>> SearchAsync(string query, DirectoryObjectKind? kind, int limit, CancellationToken ct = default);
    /// <summary>Проверка пароля bind-ом. Пустой пароль — всегда false.</summary>
    Task<bool> ValidateCredentialsAsync(string login, string password, CancellationToken ct = default);
    Task<IReadOnlyList<DirectoryTestStep>> TestConnectionAsync(CancellationToken ct = default);
}

public interface IDirectorySettingsStore
{
    Task<DirectorySettings> GetAsync(CancellationToken ct = default);
    Task SaveAsync(DirectorySettings settings, CancellationToken ct = default);
}

/// <summary>Группы пользователя AD: обновление раз в 5 минут, при недоступности домена — до 15 минут.</summary>
public interface IAdGroupCache
{
    /// <summary>null — сеанс недействителен (учётка отключена/удалена, домен выключен или недоступен дольше 15 минут).</summary>
    Task<IReadOnlyList<string>?> GetGroupsAsync(string userSid, CancellationToken ct = default);
}

public enum DirectorySignInStatus { Ok, InvalidCredentials, NoAccess, Disabled, Unavailable, NotConfigured }

public sealed record DirectorySignInResult(DirectorySignInStatus Status, DirectoryObject? Account = null);

/// <summary>Вход учёткой домена: по паролю или по уже проверенному SID (SSO).</summary>
public interface IDirectorySignIn
{
    Task<DirectorySignInResult> PasswordAsync(string login, string password, CancellationToken ct = default);
    Task<DirectorySignInResult> CompleteAsync(string userSid, CancellationToken ct = default);
}
