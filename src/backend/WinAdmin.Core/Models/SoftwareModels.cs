namespace WinAdmin.Core.Models;

public sealed record InstalledApp
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Version { get; init; }
    public string? Publisher { get; init; }
    public DateTime? InstallDate { get; init; }
    public string? InstallLocation { get; init; }
    public long? SizeBytes { get; init; }
    /// <summary>"Registry" or "Store".</summary>
    public required string Source { get; init; }
    public bool IsSystem { get; init; }
    public bool CanUninstall { get; init; }
    public string? UninstallString { get; init; }
}

public sealed record InstalledUpdate
{
    public required string Id { get; init; }
    public string? KbArticle { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public DateTime? InstalledOn { get; init; }
    public bool CanUninstall { get; init; }
    public bool CanRollback { get; init; }
}

public enum SoftwareJobType { UninstallApp, UninstallUpdate, RollbackUpdate }
public enum SoftwareJobStatus { Queued, Running, Succeeded, Failed }

public sealed record SoftwareJob
{
    public required string Id { get; init; }
    public SoftwareJobType Type { get; init; }
    public required string TargetId { get; init; }
    public required string TargetName { get; init; }
    public SoftwareJobStatus Status { get; init; }
    public int? ProgressPercent { get; init; }
    public required string StatusMessage { get; init; }
    public string? Error { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? FinishedAt { get; init; }
}

public sealed class SoftwareConflictException : Exception
{
    public string? ActiveJobId { get; }
    public SoftwareConflictException(string message, string? activeJobId = null) : base(message)
        => ActiveJobId = activeJobId;
}

public sealed class SoftwareNotFoundException : Exception
{
    public SoftwareNotFoundException(string message) : base(message) { }
}

public sealed class SoftwareActionNotAllowedException : Exception
{
    public SoftwareActionNotAllowedException(string message) : base(message) { }
}
