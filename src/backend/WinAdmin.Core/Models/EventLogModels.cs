namespace WinAdmin.Core.Models;

/// <summary>Запись журнала событий Windows, отформатированная для отображения.</summary>
public sealed record EventLogEntryDto
{
    public long Id { get; init; }
    public DateTime TimeCreated { get; init; }
    public required string LogName { get; init; }
    public string? ProviderName { get; init; }
    public int EventId { get; init; }
    public string? Level { get; init; }
    public string? LevelDisplayName { get; init; }
    public string? User { get; init; }
    public string? Message { get; init; }
    public string? MachineName { get; init; }
}

/// <summary>Параметры запроса журнала событий.</summary>
public sealed record EventLogQueryRequest
{
    public required string LogName { get; init; }
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }
    public int MaxRecords { get; init; } = 200;
    public IReadOnlyList<int>? EventIds { get; init; }
    public IReadOnlyList<string>? Levels { get; init; }
    public string? Keyword { get; init; }
    public string? User { get; init; }
    public bool ExcludeSystemAccounts { get; init; }

    /// <summary>
    /// Типы входа (LogonType) для исключения из выдачи. Например, служебные (5) и сетевые (3)
    /// входы под учётной записью SYSTEM создают постоянный фоновый шум в журнале «Авторизация».
    /// Записи без поля LogonType (например 4672 или изменения учёток) фильтр не затрагивает.
    /// </summary>
    public IReadOnlyList<int>? ExcludeLogonTypes { get; init; }

    /// <summary>
    /// Имена учётных записей из глобального чёрного списка настроек, которые полностью
    /// скрываются из выдачи журналов (например служебные/технические аккаунты, за которыми
    /// не нужно наблюдать). Сравнение регистронезависимое; учитывается как полное имя,
    /// так и часть после «DOMAIN\».
    /// </summary>
    public IReadOnlyList<string>? ExcludeUserNames { get; init; }
}

/// <summary>Результат запроса журнала: записи + признак усечения по лимиту/сканированию.</summary>
public sealed record EventLogQueryResult
{
    public required IReadOnlyList<EventLogEntryDto> Entries { get; init; }
    public bool Truncated { get; init; }
    public int ScannedCount { get; init; }
}

/// <summary>Нет прав на чтение журнала (обычно Security без прав администратора).</summary>
public sealed class EventLogAccessDeniedException : Exception
{
    public EventLogAccessDeniedException(string message) : base(message) { }
}

/// <summary>Запрошенный журнал не существует на этой машине.</summary>
public sealed class EventLogMissingException : Exception
{
    public EventLogMissingException(string message) : base(message) { }
}
