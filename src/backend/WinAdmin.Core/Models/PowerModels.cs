namespace WinAdmin.Core.Models;

/// <summary>Запрос на перезагрузку/выключение.</summary>
public sealed record PowerRequest
{
    /// <summary>Задержка перед действием, в секундах (по умолчанию 30).</summary>
    public int DelaySeconds { get; init; } = 30;

    /// <summary>Комментарий, отображаемый пользователям (для аудита).</summary>
    public string? Comment { get; init; }

    /// <summary>Принудительно закрыть приложения без ожидания.</summary>
    public bool Force { get; init; }
}

/// <summary>Результат управляющего действия.</summary>
public sealed record OperationResult
{
    public bool Success { get; init; }
    public required string Message { get; init; }

    public static OperationResult Ok(string message) => new() { Success = true, Message = message };
    public static OperationResult Fail(string message) => new() { Success = false, Message = message };
}
