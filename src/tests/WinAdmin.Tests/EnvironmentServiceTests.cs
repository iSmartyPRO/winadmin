using Microsoft.Extensions.DependencyInjection;
using Moq;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.EnvironmentChecks;
using WinAdmin.Core.Models;
using WinAdmin.Infrastructure.EnvironmentChecks;

namespace WinAdmin.Tests;

public sealed class EnvironmentServiceTests
{
    private sealed class Check(string module, Func<CheckDepth, CancellationToken, Task<IReadOnlyList<CheckResult>>> run) : IEnvironmentCheck
    {
        public string ModuleId => module;
        public Task<IReadOnlyList<CheckResult>> RunAsync(CheckDepth depth, CancellationToken ct) => run(depth, ct);
    }

    private static Check Returns(string module, params CheckResult[] results)
        => new(module, (_, _) => Task.FromResult<IReadOnlyList<CheckResult>>(results));

    private readonly Mock<IAuditService> _audit = new();
    private readonly Mock<IModuleRegistry> _modules = new();

    private EnvironmentService Service(params IEnvironmentCheck[] checks)
    {
        var sp = new ServiceCollection().AddSingleton(_audit.Object).BuildServiceProvider();
        _modules.Setup(m => m.GetState(It.IsAny<string>())).Returns((string id) =>
            new ModuleState(id, id != "off", true, null));
        return new EnvironmentService(checks, _modules.Object, sp.GetRequiredService<IServiceScopeFactory>(),
            timeout: TimeSpan.FromMilliseconds(300));
    }

    [Fact]
    public async Task Overall_is_the_worst_status_and_disabled_modules_are_skipped()
    {
        var svc = Service(
            Returns("platform", CheckResult.Ok("a", "A", "ok"), CheckResult.Warn("b", "B", "hmm")),
            Returns("ad-users", CheckResult.Fail("c", "C", "bad", "fix it")),
            Returns("off", CheckResult.Ok("d", "D", "ok")));

        var reports = await svc.RunAsync(null, CheckDepth.Quick);

        Assert.Equal(CheckStatus.Warning, reports.Single(r => r.ModuleId == "platform").Overall);
        Assert.Equal(CheckStatus.Failed, reports.Single(r => r.ModuleId == "ad-users").Overall);
        Assert.DoesNotContain(reports, r => r.ModuleId == "off");
        Assert.Equal(reports.Count, svc.Latest.Count);
    }

    [Fact]
    public async Task Explicit_module_runs_even_when_disabled()
        => Assert.Single(await Service(Returns("off", CheckResult.Ok("d", "D", "ok"))).RunAsync("off", CheckDepth.Quick));

    [Fact]
    public async Task Throwing_and_hanging_checks_do_not_break_others()
    {
        var svc = Service(
            new Check("platform", (_, _) => throw new InvalidOperationException("boom")),
            new Check("ad-users", async (_, ct) => { await Task.Delay(TimeSpan.FromSeconds(30), ct); return []; }),
            Returns("ad-folders", CheckResult.Ok("x", "X", "ok")));

        var reports = await svc.RunAsync(null, CheckDepth.Quick);

        Assert.Equal(CheckStatus.Failed, reports.Single(r => r.ModuleId == "platform").Overall);
        Assert.Contains("boom", reports.Single(r => r.ModuleId == "platform").Results[0].Message);
        Assert.Contains("время", reports.Single(r => r.ModuleId == "ad-users").Results[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(CheckStatus.Ok, reports.Single(r => r.ModuleId == "ad-folders").Overall);
    }

    [Fact]
    public async Task Status_change_is_audited_once()
    {
        var status = CheckStatus.Ok;
        var svc = Service(new Check("platform", (_, _) =>
            Task.FromResult<IReadOnlyList<CheckResult>>([new CheckResult("a", "A", status, "m")])));

        await svc.RunAsync(null, CheckDepth.Quick);   // первый результат: Ok — записываем
        await svc.RunAsync(null, CheckDepth.Quick);   // без изменений — нет
        status = CheckStatus.Failed;
        await svc.RunAsync(null, CheckDepth.Quick);   // Ok → Failed — записываем

        _audit.Verify(a => a.WriteAsync(It.Is<AuditEntryDto>(e => e.Action == "environment.status"), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public void Worst_ignores_skipped()
        => Assert.Equal(CheckStatus.Ok, EnvironmentReport.Worst([CheckResult.Ok("a", "A", ""), CheckResult.Skip("b", "B", "")]));
}
