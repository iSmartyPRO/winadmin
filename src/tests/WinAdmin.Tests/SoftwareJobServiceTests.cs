using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Infrastructure.Software;

namespace WinAdmin.Tests;

internal sealed class FakeCatalog : ISoftwareCatalogService
{
    public List<InstalledApp> Apps { get; } = [];
    public List<InstalledUpdate> Updates { get; } = [];

    public IReadOnlyList<InstalledApp> GetApplications() => Apps;
    public IReadOnlyList<InstalledUpdate> GetUpdates() => Updates;
    public InstalledApp? FindApplication(string id) => Apps.FirstOrDefault(a => a.Id == id);
    public InstalledUpdate? FindUpdate(string id) => Updates.FirstOrDefault(u => u.Id == id);
}

internal sealed class FakeRunner : ISoftwareProcessRunner
{
    public int ExitCode { get; set; }
    public Exception? ThrowOnRun { get; set; }
    public TaskCompletionSource? Gate { get; set; }
    public bool CancellationObserved { get; private set; }
    public List<(string FileName, string Arguments)> Runs { get; } = [];
    public List<string> RemovedStorePackages { get; } = [];

    public async Task<int> RunAsync(string fileName, string arguments, CancellationToken ct)
    {
        Runs.Add((fileName, arguments));
        try
        {
            if (Gate is not null)
                await Gate.Task.WaitAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            CancellationObserved = true;
            throw;
        }
        if (ThrowOnRun is not null)
            throw ThrowOnRun;
        return ExitCode;
    }

    public Task RemoveStorePackageAsync(string fullName, CancellationToken ct)
    {
        RemovedStorePackages.Add(fullName);
        return Task.CompletedTask;
    }
}

internal sealed class FakeAudit : IAuditService
{
    public List<AuditEntryDto> Entries { get; } = [];
    public TaskCompletionSource? Gate { get; set; }
    public TaskCompletionSource WriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task WriteAsync(AuditEntryDto entry, CancellationToken ct = default)
    {
        WriteStarted.TrySetResult();
        if (Gate is not null)
            await Gate.Task.WaitAsync(ct);
        Entries.Add(entry);
    }

    public Task<IReadOnlyList<AuditEntryDto>> QueryAsync(int limit = 200, string? actor = null, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AuditEntryDto>>(Entries);
}

internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public ManualTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan interval) => _utcNow += interval;
}

public sealed class SoftwareJobServiceTests
{
    private static (SoftwareJobService Svc, FakeCatalog Catalog, FakeRunner Runner, FakeAudit Audit, ManualTimeProvider Time) Create(TimeSpan? jobTimeout = null)
    {
        var catalog = new FakeCatalog();
        var runner = new FakeRunner();
        var audit = new FakeAudit();
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 7, 12, 10, 0, 0, TimeSpan.Zero));

        var services = new ServiceCollection();
        services.AddSingleton<ISoftwareCatalogService>(catalog);
        services.AddSingleton<IAuditService>(audit);
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var svc = new SoftwareJobService(scopeFactory, runner, NullLogger<SoftwareJobService>.Instance, time, jobTimeout);

        return (svc, catalog, runner, audit, time);
    }

    [Fact]
    public async Task StartUninstallApp_Succeeds_AndAuditsOnCompletion()
    {
        var (svc, catalog, runner, audit, _) = Create();
        catalog.Apps.Add(new InstalledApp
        {
            Id = "reg:App1",
            Name = "App1",
            Source = "Registry",
            CanUninstall = true,
            UninstallString = "cmd.exe /c exit 0",
        });
        runner.ExitCode = 0;
        runner.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var job = svc.StartUninstallApp("reg:App1");

        Assert.Empty(audit.Entries);
        runner.Gate.SetResult();
        var done = await WaitFor(svc, job.Id, SoftwareJobStatus.Succeeded);
        Assert.Equal(100, done.ProgressPercent);
        Assert.Contains(audit.Entries, e =>
            e.Action == "software.app.uninstall" &&
            e.Target == "App1" &&
            e.Details?.Contains("reg:App1", StringComparison.OrdinalIgnoreCase) == true &&
            e.Success);
    }

    [Fact]
    public async Task SucceededStatus_IsVisibleOnlyAfterAuditWriteCompletes()
    {
        var (svc, catalog, runner, audit, _) = Create();
        catalog.Apps.Add(new InstalledApp
        {
            Id = "reg:App1",
            Name = "App1",
            Source = "Registry",
            CanUninstall = true,
            UninstallString = "cmd.exe /c exit 0",
        });
        runner.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        audit.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var job = svc.StartUninstallApp("reg:App1");
        runner.Gate.SetResult();
        await audit.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var visible = Assert.IsType<SoftwareJob>(svc.GetJob(job.Id));
        Assert.NotEqual(SoftwareJobStatus.Succeeded, visible.Status);

        audit.Gate.SetResult();
        await WaitFor(svc, job.Id, SoftwareJobStatus.Succeeded);
        Assert.Single(audit.Entries);
    }

    [Fact]
    public async Task Timeout_FailsCancelsRunnerAndAllowsSecondStartOnlyAfterFinished()
    {
        var (svc, catalog, runner, _, _) = Create(TimeSpan.FromMilliseconds(50));
        catalog.Apps.Add(new InstalledApp
        {
            Id = "reg:App1",
            Name = "App1",
            Source = "Registry",
            CanUninstall = true,
            UninstallString = "cmd.exe /c exit 0",
        });
        runner.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = svc.StartUninstallApp("reg:App1");
        await WaitFor(svc, first.Id, SoftwareJobStatus.Running);
        var conflict = Assert.Throws<SoftwareConflictException>(() => svc.StartUninstallApp("reg:App1"));
        Assert.Equal(first.Id, conflict.ActiveJobId);

        var failed = await WaitFor(svc, first.Id, SoftwareJobStatus.Failed);
        Assert.Contains("тайм-аут", failed.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("тайм-аут", failed.Error, StringComparison.OrdinalIgnoreCase);
        Assert.True(runner.CancellationObserved);

        runner.Gate = null;
        var second = svc.StartUninstallApp("reg:App1");
        Assert.NotEqual(first.Id, second.Id);
        await WaitFor(svc, second.Id, SoftwareJobStatus.Succeeded);
    }

    [Fact]
    public void SecondStart_WhileActive_ThrowsConflictWithActiveJobId()
    {
        var (svc, catalog, runner, _, _) = Create();
        catalog.Apps.Add(new InstalledApp
        {
            Id = "reg:App1",
            Name = "App1",
            Source = "Registry",
            CanUninstall = true,
            UninstallString = "cmd.exe /c exit 0",
        });
        runner.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = svc.StartUninstallApp("reg:App1");
        var ex = Assert.Throws<SoftwareConflictException>(() => svc.StartUninstallApp("reg:App1"));

        Assert.Equal(first.Id, ex.ActiveJobId);
        runner.Gate.SetResult();
    }

    [Fact]
    public void SecondStart_WhileActive_ThrowsConflictBeforeTargetValidation()
    {
        var (svc, catalog, runner, _, _) = Create();
        catalog.Apps.Add(new InstalledApp
        {
            Id = "reg:App1",
            Name = "App1",
            Source = "Registry",
            CanUninstall = true,
            UninstallString = "cmd.exe /c exit 0",
        });
        runner.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = svc.StartUninstallApp("reg:App1");
        var ex = Assert.Throws<SoftwareConflictException>(() => svc.StartUninstallApp("reg:missing"));

        Assert.Equal(first.Id, ex.ActiveJobId);
        runner.Gate.SetResult();
    }

    [Fact]
    public void UnknownApp_ThrowsNotFound()
    {
        var (svc, _, _, _, _) = Create();

        Assert.Throws<SoftwareNotFoundException>(() => svc.StartUninstallApp("reg:missing"));
    }

    [Fact]
    public void CannotUninstallApp_ThrowsNotAllowed()
    {
        var (svc, catalog, _, _, _) = Create();
        catalog.Apps.Add(new InstalledApp
        {
            Id = "reg:Locked",
            Name = "Locked",
            Source = "Registry",
            CanUninstall = false,
        });

        Assert.Throws<SoftwareActionNotAllowedException>(() => svc.StartUninstallApp("reg:Locked"));
    }

    [Fact]
    public async Task ExitCode3010_Succeeds_WithRebootMessage()
    {
        var (svc, catalog, runner, _, _) = Create();
        catalog.Apps.Add(new InstalledApp
        {
            Id = "reg:App1",
            Name = "App1",
            Source = "Registry",
            CanUninstall = true,
            UninstallString = "cmd.exe /c exit 0",
        });
        runner.ExitCode = 3010;

        var job = svc.StartUninstallApp("reg:App1");
        var done = await WaitFor(svc, job.Id, SoftwareJobStatus.Succeeded);

        Assert.Contains("перезагруз", done.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StoreApp_Uninstall_RemovesStorePackage()
    {
        var (svc, catalog, runner, _, _) = Create();
        catalog.Apps.Add(new InstalledApp
        {
            Id = SoftwareAppHelpers.MakeStoreId("Vendor.App_1.0.0.0_x64__publisher"),
            Name = "Store App",
            Source = "Store",
            CanUninstall = true,
        });

        var job = svc.StartUninstallApp(SoftwareAppHelpers.MakeStoreId("Vendor.App_1.0.0.0_x64__publisher"));
        await WaitFor(svc, job.Id, SoftwareJobStatus.Succeeded);

        Assert.Equal(["Vendor.App_1.0.0.0_x64__publisher"], runner.RemovedStorePackages);
    }

    [Fact]
    public async Task StartUninstallUpdate_RunsWusa_AndAudits()
    {
        var (svc, catalog, runner, audit, _) = Create();
        catalog.Updates.Add(new InstalledUpdate
        {
            Id = SoftwareAppHelpers.MakeUpdateId("KB5025221"),
            KbArticle = "KB5025221",
            Title = "Security Update",
            CanUninstall = true,
        });

        var job = svc.StartUninstallUpdate(SoftwareAppHelpers.MakeUpdateId("KB5025221"));
        await WaitFor(svc, job.Id, SoftwareJobStatus.Succeeded);

        var run = Assert.Single(runner.Runs);
        Assert.Equal("wusa.exe", run.FileName);
        Assert.Contains("/kb:5025221", run.Arguments, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(audit.Entries, e => e.Action == "software.update.uninstall" && e.Success);
    }

    [Fact]
    public async Task StartRollbackUpdate_RunsWusa_AndAuditsRollback()
    {
        var (svc, catalog, _, audit, _) = Create();
        catalog.Updates.Add(new InstalledUpdate
        {
            Id = SoftwareAppHelpers.MakeUpdateId("KB5025221"),
            KbArticle = "KB5025221",
            Title = "Security Update",
            CanRollback = true,
        });

        var job = svc.StartRollbackUpdate(SoftwareAppHelpers.MakeUpdateId("KB5025221"));
        await WaitFor(svc, job.Id, SoftwareJobStatus.Succeeded);

        Assert.Contains(audit.Entries, e => e.Action == "software.update.rollback" && e.Success);
    }

    [Fact]
    public async Task CompletedJobs_OlderThanOneHour_ArePruned()
    {
        var (svc, catalog, _, _, time) = Create();
        catalog.Apps.Add(new InstalledApp
        {
            Id = "reg:App1",
            Name = "App1",
            Source = "Registry",
            CanUninstall = true,
            UninstallString = "cmd.exe /c exit 0",
        });
        var job = svc.StartUninstallApp("reg:App1");
        await WaitFor(svc, job.Id, SoftwareJobStatus.Succeeded);

        time.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1));

        Assert.Null(svc.GetJob(job.Id));
    }

    private static async Task<SoftwareJob> WaitFor(ISoftwareJobService svc, string id, SoftwareJobStatus status)
    {
        for (var i = 0; i < 100; i++)
        {
            var job = svc.GetJob(id);
            if (job is not null && job.Status == status)
                return job;
            if (job is not null && job.Status is SoftwareJobStatus.Failed or SoftwareJobStatus.Succeeded)
                return job;
            await Task.Delay(50);
        }

        throw new TimeoutException();
    }
}
