namespace WinAdmin.Core.Models;

/// <summary>Представление API-ключа для UI (без секрета).</summary>
public sealed record ApiKeyDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public DateTimeOffset? LastUsedAt { get; init; }
    public bool IsRevoked { get; init; }

    /// <summary>Последние 4 символа ключа — для опознания в списке.</summary>
    public string? Hint { get; init; }
}

/// <summary>Только что созданный ключ — сырой секрет показывается один раз.</summary>
public sealed record CreatedApiKey
{
    public required ApiKeyDto Key { get; init; }
    public required string PlaintextKey { get; init; }
}

/// <summary>Результат проверки ключа — личность для авторизации.</summary>
public sealed record ApiKeyPrincipal
{
    public required string Id { get; init; }
    public required string Name { get; init; }
}

/// <summary>Запрос на создание ключа.</summary>
public sealed record CreateApiKeyRequest
{
    public required string Name { get; init; }
    public List<string> RoleIds { get; init; } = [];
    public DateTimeOffset? ExpiresAt { get; init; }
}

/// <summary>Запись журнала аудита.</summary>
public sealed record AuditEntryDto
{
    public long Id { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public required string Actor { get; init; }
    public required string Action { get; init; }
    public string? Target { get; init; }
    public bool Success { get; init; }
    public string? Details { get; init; }
    public string? SourceIp { get; init; }
}
