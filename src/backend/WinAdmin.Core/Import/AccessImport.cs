namespace WinAdmin.Core.Import;

/// <summary>
/// Строка app_settings портала Access. ServicePassword — открытый текст из БД Access:
/// не попадает ни в ToString, ни в отчёт.
/// </summary>
public sealed record AccessAppSettings(
    string Domain, string LdapPath, string ServiceDomain, string ServiceUsername, string ServicePassword,
    string ExchangeUrl, string FiredUsersGroup, bool HasApiSettings)
{
    public override string ToString()
        => $"AccessAppSettings {{ Domain = {Domain}, LdapPath = {LdapPath}, ServiceUsername = {ServiceUsername}, " +
           $"ServicePasswordSet = {ServicePassword.Length > 0} }}";
}

public sealed record AccessTerminatedOu(string OuName, string? OuDn, string? DisplayName, bool IsDeactivationDefault);

/// <summary>Назначение роли Access пользователю или группе AD (principal_type: user / group).</summary>
public sealed record AccessRolePrincipal(
    string RoleCode, string PrincipalType, string? SamAccountName, string DistinguishedName, string DisplayName);

/// <summary>Override роли AD-пользователя (portal_users).</summary>
public sealed record AccessPortalUser(string SamAccountName, string RoleCode, bool IsActive);

/// <summary>Локальная учётка Access (хеш пароля не читается — не переносится).</summary>
public sealed record AccessLocalUser(string Username, string RoleCode, bool IsActive);

public sealed record AccessAuditEntry(
    long Id, DateTimeOffset CreatedAt, string ActorSam, string Action, string? TargetType, string? TargetId, string? DetailsJson);

/// <summary>Что в Access есть, но не переносится: метаданные проектов, рассылки проектов.</summary>
public sealed record AccessExtras(int ProjectMetadata, int MailGroups);

/// <summary>Чтение данных портала Access (PostgreSQL). Отсутствующие в старой схеме таблицы/колонки — пустые значения.</summary>
public interface IAccessSource
{
    /// <summary>Откуда читаем — для отчёта (без пароля).</summary>
    string Description { get; }
    Task<AccessAppSettings?> GetAppSettingsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetExcludedOusAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AccessTerminatedOu>> GetTerminatedOusAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AccessRolePrincipal>> GetRolePrincipalsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AccessPortalUser>> GetPortalUsersAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AccessLocalUser>> GetLocalUsersAsync(CancellationToken ct = default);
    Task<AccessExtras> GetExtrasAsync(CancellationToken ct = default);
    Task<long> CountAuditAsync(long afterId, CancellationToken ct = default);
    /// <summary>Записи с id &gt; afterId по возрастанию id, не больше limit.</summary>
    Task<IReadOnlyList<AccessAuditEntry>> ReadAuditAsync(long afterId, int limit, CancellationToken ct = default);
}

public sealed record AccessImportOptions(bool Apply = false, bool Audit = false, bool Overwrite = false, bool ImportServicePassword = false);

public enum ImportStatus { Created, WillCreate, Exists, Conflict, NotFoundInAd, NotImported }

public sealed record ImportRow(string What, string From, string To, ImportStatus Status, string? Note = null);

public sealed class AccessImportReport
{
    public required string Source { get; init; }
    public bool Applied { get; init; }
    public List<ImportRow> Rows { get; } = [];
    public List<string> Hints { get; } = [];

    public int Count(ImportStatus status) => Rows.Count(r => r.Status == status);

    public static string Text(ImportStatus status) => status switch
    {
        ImportStatus.Created => "Создано",
        ImportStatus.WillCreate => "Будет создано",
        ImportStatus.Exists => "Пропущено — уже есть",
        ImportStatus.Conflict => "Конфликт",
        ImportStatus.NotFoundInAd => "Не найдено в AD",
        _ => "Не импортируется",
    };

    /// <summary>«Создано: 3, Конфликт: 1, …» — только ненулевые.</summary>
    public string Summary()
        => string.Join(", ", Enum.GetValues<ImportStatus>().Where(s => Count(s) > 0).Select(s => $"{Text(s)}: {Count(s)}"));
}
