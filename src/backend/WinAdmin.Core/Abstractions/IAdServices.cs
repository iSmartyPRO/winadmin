using WinAdmin.Core.ActiveDirectory.Users;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.EnvironmentChecks;

namespace WinAdmin.Core.Abstractions;

/// <summary>Структура каталога и учётка записи (PlatformSettings, ключ «ad-structure»).</summary>
public interface IAdStructureStore
{
    Task<AdStructureSettings> GetAsync(CancellationToken ct = default);

    /// <summary>newPassword: null — не менять, "" — удалить, иначе — зашифровать и сохранить.</summary>
    Task SaveAsync(AdStructureSettings settings, string? newPassword, CancellationToken ct = default);

    /// <summary>Учётка записи с расшифрованным паролем; SecretUnavailableException — пароль не расшифровывается.</summary>
    Task<AdWriteCredential> GetWriteCredentialAsync(CancellationToken ct = default);
}

/// <summary>Чтение AD для модулей (учётка компьютера; эффективные права — от имени учётки записи).</summary>
public interface IAdReader
{
    /// <summary>OU первого уровня под RootOu; includeHidden=false — без скрытых.</summary>
    Task<IReadOnlyList<AdProject>> ListProjectsAsync(bool includeHidden = false, CancellationToken ct = default);
    Task<bool> ExistsAsync(string dn, CancellationToken ct = default);
    Task<EffectiveRights> ReadEffectiveAsync(string dn, CancellationToken ct = default);
    Task<WriterStatus> GetWriterStatusAsync(CancellationToken ct = default);
}

/// <summary>Запись в AD учёткой записи. Ошибки — AdWriteException / DirectoryUnavailableException.</summary>
public interface IAdWriter
{
    Task ModifyAttributesAsync(string dn, IReadOnlyDictionary<string, string?> changes, CancellationToken ct = default);
    /// <summary>false — уже был участником.</summary>
    Task<bool> AddMemberAsync(string groupDn, string memberDn, CancellationToken ct = default);
    /// <summary>false — не был участником.</summary>
    Task<bool> RemoveMemberAsync(string groupDn, string memberDn, CancellationToken ct = default);
    Task SetPrimaryGroupAsync(string userDn, string groupSid, CancellationToken ct = default);
    Task SetEnabledAsync(string userDn, bool enabled, CancellationToken ct = default);
    Task ResetPasswordAsync(string userDn, string password, bool mustChange, CancellationToken ct = default);
    /// <summary>Возвращает новый DN.</summary>
    Task<string> MoveAsync(string dn, string targetOuDn, CancellationToken ct = default);
    /// <summary>Глобальная группа безопасности; возвращает DN.</summary>
    Task<string> CreateGroupAsync(string ouDn, string cn, string description, CancellationToken ct = default);
    Task SetPhotoAsync(string userDn, byte[]? photo, CancellationToken ct = default);
}

/// <summary>Проверка окружения: запуск, последние результаты.</summary>
public interface IEnvironmentService
{
    Task<IReadOnlyList<EnvironmentReport>> RunAsync(string? moduleId, CheckDepth depth, CancellationToken ct = default);
    IReadOnlyList<EnvironmentReport> Latest { get; }
}

/// <summary>Чтение пользователей и групп для модуля «Пользователи AD» (учётка компьютера).</summary>
public interface IAdUserDirectory
{
    Task<IReadOnlyList<AdUser>> ListUsersAsync(string baseDn, IReadOnlyCollection<string> attributes, CancellationToken ct = default);
    Task<AdUser?> FindUserAsync(string sam, IReadOnlyCollection<string> attributes, CancellationToken ct = default);
    Task<IReadOnlyList<AdGroupRef>> GetGroupsAsync(AdUser user, CancellationToken ct = default);
    Task<byte[]?> GetPhotoAsync(string dn, CancellationToken ct = default);
    Task<AdGroupRef?> FindGroupAsync(string nameOrDn, CancellationToken ct = default);
    Task<AdGroupRef> GetDomainUsersGroupAsync(CancellationToken ct = default);
    Task<string?> FindSampleUserAsync(string ouDn, CancellationToken ct = default);
    /// <summary>SID учётки записи, её групп (tokenGroups) и Everyone / Authenticated Users — для разбора ACL.</summary>
    Task<IReadOnlyList<string>> GetWriterSidsAsync(CancellationToken ct = default);
    Task<byte[]?> ReadSecurityDescriptorAsync(string dn, CancellationToken ct = default);
}
