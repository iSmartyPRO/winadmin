namespace WinAdmin.Core.Models;

/// <summary>Windows-служба.</summary>
public sealed record ServiceInfo
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>Running, Stopped, Paused, StartPending, StopPending, …</summary>
    public required string Status { get; init; }

    /// <summary>Automatic, Manual, Disabled.</summary>
    public string StartType { get; init; } = "Unknown";
    public bool CanStop { get; init; }
    public bool CanPauseAndContinue { get; init; }
    public string? Account { get; init; }
}

/// <summary>Действие над службой.</summary>
public enum ServiceAction
{
    Start,
    Stop,
    Restart,
}
