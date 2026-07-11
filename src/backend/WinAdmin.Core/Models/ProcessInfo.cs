namespace WinAdmin.Core.Models;

/// <summary>Запущенный процесс / приложение.</summary>
public sealed record ProcessInfo
{
    public int Pid { get; init; }
    public required string Name { get; init; }
    public string? MainWindowTitle { get; init; }
    public long WorkingSetBytes { get; init; }
    public int ThreadCount { get; init; }
    public DateTimeOffset? StartTime { get; init; }

    /// <summary>true, если у процесса есть видимое главное окно (приложение).</summary>
    public bool HasWindow { get; init; }
}
