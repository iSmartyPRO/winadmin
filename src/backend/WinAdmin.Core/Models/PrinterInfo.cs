namespace WinAdmin.Core.Models;

/// <summary>Принтер и состояние его очереди.</summary>
public sealed record PrinterInfo
{
    public required string Name { get; init; }
    public string? PortName { get; init; }
    public string? DriverName { get; init; }
    public string? Location { get; init; }
    public bool IsDefault { get; init; }
    public bool IsShared { get; init; }
    public bool WorkOffline { get; init; }

    /// <summary>Idle, Printing, Paused, Error, …</summary>
    public string Status { get; init; } = "Unknown";

    /// <summary>Количество заданий в очереди.</summary>
    public int QueuedJobs { get; init; }
}

/// <summary>Действие над принтером.</summary>
public enum PrinterAction
{
    Pause,
    Resume,
    Purge,
}
