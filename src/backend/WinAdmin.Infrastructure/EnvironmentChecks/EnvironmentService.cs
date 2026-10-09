using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.EnvironmentChecks;
using WinAdmin.Core.Models;

namespace WinAdmin.Infrastructure.EnvironmentChecks;

/// <summary>Запуск проверок окружения: «platform» — всегда, модули — если включены (или запрошены явно).</summary>
public sealed class EnvironmentService(
    IEnumerable<IEnvironmentCheck> checks, IModuleRegistry modules, IServiceScopeFactory scopes,
    TimeProvider? time = null, TimeSpan? timeout = null) : IEnvironmentService
{
    public const string Platform = "platform";
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(60);
    private readonly ConcurrentDictionary<string, EnvironmentReport> _latest = new();

    public IReadOnlyList<EnvironmentReport> Latest => _latest.Values.OrderBy(r => r.ModuleId == Platform ? "" : r.ModuleId).ToList();

    public async Task<IReadOnlyList<EnvironmentReport>> RunAsync(string? moduleId, CheckDepth depth, CancellationToken ct = default)
    {
        var selected = checks.Where(c => moduleId is null
                ? c.ModuleId == Platform || modules.GetState(c.ModuleId).Enabled
                : c.ModuleId == moduleId)
            .GroupBy(c => c.ModuleId);

        var reports = await Task.WhenAll(selected.Select(group => RunModuleAsync(group.Key, group, depth, ct)));
        foreach (var report in reports) await RememberAsync(report, ct);
        return reports.OrderBy(r => r.ModuleId == Platform ? "" : r.ModuleId).ToList();
    }

    private async Task<EnvironmentReport> RunModuleAsync(string moduleId, IEnumerable<IEnvironmentCheck> group, CheckDepth depth, CancellationToken ct)
    {
        var results = new List<CheckResult>();
        foreach (var check in group)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(depth == CheckDepth.Full ? _timeout * 10 : _timeout);
            try
            {
                results.AddRange(await check.RunAsync(depth, cts.Token).WaitAsync(cts.Token));
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                results.Add(CheckResult.Fail($"{moduleId}.timeout", "Проверка", "Проверка не уложилась во время ожидания"));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results.Add(CheckResult.Fail($"{moduleId}.error", "Проверка", "Проверка не выполнилась: " + ex.Message));
            }
        }
        return new EnvironmentReport(moduleId, _time.GetUtcNow(), EnvironmentReport.Worst(results), results);
    }

    private async Task RememberAsync(EnvironmentReport report, CancellationToken ct)
    {
        _latest.TryGetValue(report.ModuleId, out var previous);
        _latest[report.ModuleId] = report;
        if (previous?.Overall == report.Overall) return;
        using var scope = scopes.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAuditService>().WriteAsync(new AuditEntryDto
        {
            Actor = "system", Action = "environment.status", Target = report.ModuleId, Success = report.Overall != CheckStatus.Failed,
            Details = $"{previous?.Overall.ToString() ?? "—"} → {report.Overall}",
        }, ct);
    }
}
