namespace WinAdmin.Core.Models;

/// <summary>Учётная запись из чёрного списка, скрываемая из журналов событий.</summary>
public sealed record ExcludedUserDto
{
    public required string Id { get; init; }
    public required string UserName { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Запрос на добавление учётной записи в чёрный список.</summary>
public sealed record AddExcludedUserRequest
{
    public string UserName { get; init; } = "";
}
