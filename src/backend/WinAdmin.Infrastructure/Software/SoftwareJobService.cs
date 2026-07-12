using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;

namespace WinAdmin.Infrastructure.Software;

public sealed class SoftwareJobService : ISoftwareJobService
{
    private static readonly TimeSpan JobTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan CompletedJobRetention = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<string, SoftwareJobState> _jobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISoftwareProcessRunner _runner;
    private readonly ILogger<SoftwareJobService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly object _gate = new();

    public SoftwareJobService(
        IServiceScopeFactory scopeFactory,
        ISoftwareProcessRunner runner,
        ILogger<SoftwareJobService> logger,
        TimeProvider? timeProvider = null)
    {
        _scopeFactory = scopeFactory;
        _runner = runner;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public SoftwareJob StartUninstallApp(string appId)
    {
        ThrowIfActive();
        using var scope = _scopeFactory.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<ISoftwareCatalogService>();
        var app = catalog.FindApplication(appId)
            ?? throw new SoftwareNotFoundException($"Application '{appId}' was not found.");
        if (!app.CanUninstall)
            throw new SoftwareActionNotAllowedException($"Application '{appId}' cannot be uninstalled.");

        var command = CreateAppCommand(app);
        return StartJob(SoftwareJobType.UninstallApp, app.Id, app.Name, command, "software.app.uninstall");
    }

    public SoftwareJob StartUninstallUpdate(string updateId)
    {
        ThrowIfActive();
        using var scope = _scopeFactory.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<ISoftwareCatalogService>();
        var update = catalog.FindUpdate(updateId)
            ?? throw new SoftwareNotFoundException($"Update '{updateId}' was not found.");
        if (!update.CanUninstall)
            throw new SoftwareActionNotAllowedException($"Update '{updateId}' cannot be uninstalled.");

        return StartJob(
            SoftwareJobType.UninstallUpdate,
            update.Id,
            update.Title,
            CreateUpdateCommand(update),
            "software.update.uninstall");
    }

    public SoftwareJob StartRollbackUpdate(string updateId)
    {
        ThrowIfActive();
        using var scope = _scopeFactory.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<ISoftwareCatalogService>();
        var update = catalog.FindUpdate(updateId)
            ?? throw new SoftwareNotFoundException($"Update '{updateId}' was not found.");
        if (!update.CanRollback)
            throw new SoftwareActionNotAllowedException($"Update '{updateId}' cannot be rolled back.");

        return StartJob(
            SoftwareJobType.RollbackUpdate,
            update.Id,
            update.Title,
            CreateUpdateCommand(update),
            "software.update.rollback");
    }

    public SoftwareJob? GetJob(string jobId)
    {
        PruneCompletedJobs();
        return _jobs.TryGetValue(jobId, out var state) ? Snapshot(state) : null;
    }

    public SoftwareJob? GetActiveJob()
    {
        PruneCompletedJobs();
        return _jobs.Values
            .Where(IsActive)
            .OrderBy(j => j.StartedAt)
            .Select(Snapshot)
            .FirstOrDefault();
    }

    private SoftwareJob StartJob(
        SoftwareJobType type,
        string targetId,
        string targetName,
        SoftwareJobCommand command,
        string auditAction)
    {
        SoftwareJobState state;
        lock (_gate)
        {
            PruneCompletedJobs();
            var active = _jobs.Values.FirstOrDefault(IsActive);
            if (active != null)
                throw new SoftwareConflictException("Another software job is already active.", active.Id);

            state = new SoftwareJobState
            {
                Id = Guid.NewGuid().ToString("N"),
                Type = type,
                TargetId = targetId,
                TargetName = targetName,
                Status = SoftwareJobStatus.Queued,
                StatusMessage = "Запуск...",
                StartedAt = _timeProvider.GetUtcNow().UtcDateTime,
                Command = command,
                AuditAction = auditAction,
            };
            _jobs[state.Id] = state;
        }

        _ = Task.Run(() => RunJobAsync(state));
        return Snapshot(state);
    }

    private void ThrowIfActive()
    {
        lock (_gate)
        {
            PruneCompletedJobs();
            var active = _jobs.Values.FirstOrDefault(IsActive);
            if (active != null)
                throw new SoftwareConflictException("Another software job is already active.", active.Id);
        }
    }

    private async Task RunJobAsync(SoftwareJobState state)
    {
        using var timeout = new CancellationTokenSource(JobTimeout);
        var success = false;
        string? details = null;

        try
        {
            UpdateState(state, s =>
            {
                s.Status = SoftwareJobStatus.Running;
                s.StatusMessage = "Удаление...";
            });

            var exitCode = state.Command.Kind == SoftwareJobCommandKind.StorePackage
                ? await RemoveStorePackageAsync(state.Command, timeout.Token)
                : await _runner.RunAsync(state.Command.FileName, state.Command.Arguments, timeout.Token);

            success = exitCode is 0 or 3010;
            details = exitCode == 3010
                ? "Операция завершена успешно, требуется перезагрузка."
                : success
                    ? "Операция завершена успешно."
                    : $"Операция завершилась с кодом {exitCode}.";

            UpdateState(state, s =>
            {
                s.Status = success ? SoftwareJobStatus.Succeeded : SoftwareJobStatus.Failed;
                s.ProgressPercent = 100;
                s.StatusMessage = details;
                s.Error = success ? null : details;
                s.FinishedAt = _timeProvider.GetUtcNow().UtcDateTime;
            });
        }
        catch (OperationCanceledException ex) when (timeout.IsCancellationRequested)
        {
            details = "Операция прервана по тайм-ауту.";
            _logger.LogWarning(ex, "Software job {JobId} timed out", state.Id);
            UpdateFailure(state, details);
        }
        catch (Exception ex)
        {
            details = ex.Message;
            _logger.LogError(ex, "Software job {JobId} failed", state.Id);
            UpdateFailure(state, details);
        }

        await WriteAuditAsync(state, success, details);
    }

    private async Task<int> RemoveStorePackageAsync(SoftwareJobCommand command, CancellationToken ct)
    {
        await _runner.RemoveStorePackageAsync(command.PackageFullName, ct);
        return 0;
    }

    private void UpdateFailure(SoftwareJobState state, string details)
    {
        UpdateState(state, s =>
        {
            s.Status = SoftwareJobStatus.Failed;
            s.ProgressPercent = 100;
            s.StatusMessage = "Завершение...";
            s.Error = details;
            s.FinishedAt = _timeProvider.GetUtcNow().UtcDateTime;
        });
    }

    private async Task WriteAuditAsync(SoftwareJobState state, bool success, string? details)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
            await audit.WriteAsync(new AuditEntryDto
            {
                Actor = "system",
                Action = state.AuditAction,
                Target = state.TargetId,
                Success = success,
                Details = details,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write audit entry for software job {JobId}", state.Id);
        }
    }

    private void PruneCompletedJobs()
    {
        var cutoff = _timeProvider.GetUtcNow().UtcDateTime - CompletedJobRetention;
        foreach (var pair in _jobs)
        {
            var job = pair.Value;
            if (!IsActive(job) && job.FinishedAt.HasValue && job.FinishedAt.Value < cutoff)
                _jobs.TryRemove(pair.Key, out _);
        }
    }

    private static SoftwareJobCommand CreateAppCommand(InstalledApp app)
    {
        if (string.Equals(app.Source, "Store", StringComparison.OrdinalIgnoreCase))
        {
            if (!SoftwareAppHelpers.TryParseId(app.Id, out var kind, out var fullName) || kind != SoftwareIdKind.Store)
                throw new SoftwareActionNotAllowedException($"Store application '{app.Id}' has an invalid package id.");

            return SoftwareJobCommand.StorePackage(fullName);
        }

        if (string.IsNullOrWhiteSpace(app.UninstallString))
            throw new SoftwareActionNotAllowedException($"Application '{app.Id}' does not have an uninstall command.");

        var (fileName, arguments) = SplitCommand(app.UninstallString);
        return SoftwareJobCommand.Process(fileName, arguments);
    }

    private static SoftwareJobCommand CreateUpdateCommand(InstalledUpdate update)
    {
        var kb = update.KbArticle;
        if (string.IsNullOrWhiteSpace(kb) &&
            SoftwareAppHelpers.TryParseId(update.Id, out var kind, out var key) &&
            kind == SoftwareIdKind.Update)
        {
            kb = key;
        }

        if (string.IsNullOrWhiteSpace(kb))
            throw new SoftwareActionNotAllowedException($"Update '{update.Id}' does not have a KB article.");

        if (kb.StartsWith("KB", StringComparison.OrdinalIgnoreCase))
            kb = kb[2..];

        return SoftwareJobCommand.Process("wusa.exe", $"/uninstall /kb:{kb} /quiet /norestart");
    }

    private static (string FileName, string Arguments) SplitCommand(string command)
    {
        var trimmed = command.Trim();
        if (trimmed.Length == 0)
            throw new SoftwareActionNotAllowedException("Uninstall command is empty.");

        if (trimmed[0] == '"')
        {
            var endQuote = trimmed.IndexOf('"', 1);
            if (endQuote < 0)
                return (trimmed.Trim('"'), string.Empty);

            return (trimmed[1..endQuote], trimmed[(endQuote + 1)..].TrimStart());
        }

        var split = trimmed.IndexOf(' ');
        return split < 0
            ? (trimmed, string.Empty)
            : (trimmed[..split], trimmed[(split + 1)..].TrimStart());
    }

    private static bool IsActive(SoftwareJobState state)
        => state.Status is SoftwareJobStatus.Queued or SoftwareJobStatus.Running;

    private static SoftwareJob Snapshot(SoftwareJobState state)
    {
        lock (state.Sync)
        {
            return new SoftwareJob
            {
                Id = state.Id,
                Type = state.Type,
                TargetId = state.TargetId,
                TargetName = state.TargetName,
                Status = state.Status,
                ProgressPercent = state.ProgressPercent,
                StatusMessage = state.StatusMessage,
                Error = state.Error,
                StartedAt = state.StartedAt,
                FinishedAt = state.FinishedAt,
            };
        }
    }

    private static void UpdateState(SoftwareJobState state, Action<SoftwareJobState> update)
    {
        lock (state.Sync)
        {
            update(state);
        }
    }

    private sealed class SoftwareJobState
    {
        public object Sync { get; } = new();
        public required string Id { get; init; }
        public SoftwareJobType Type { get; init; }
        public required string TargetId { get; init; }
        public required string TargetName { get; init; }
        public SoftwareJobStatus Status { get; set; }
        public int? ProgressPercent { get; set; }
        public required string StatusMessage { get; set; }
        public string? Error { get; set; }
        public DateTime StartedAt { get; init; }
        public DateTime? FinishedAt { get; set; }
        public required SoftwareJobCommand Command { get; init; }
        public required string AuditAction { get; init; }
    }

    private sealed record SoftwareJobCommand(
        SoftwareJobCommandKind Kind,
        string FileName,
        string Arguments,
        string PackageFullName)
    {
        public static SoftwareJobCommand Process(string fileName, string arguments)
            => new(SoftwareJobCommandKind.Process, fileName, arguments, string.Empty);

        public static SoftwareJobCommand StorePackage(string packageFullName)
            => new(SoftwareJobCommandKind.StorePackage, string.Empty, string.Empty, packageFullName);
    }

    private enum SoftwareJobCommandKind
    {
        Process,
        StorePackage,
    }
}
