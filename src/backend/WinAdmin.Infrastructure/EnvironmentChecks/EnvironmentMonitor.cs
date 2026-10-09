using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.EnvironmentChecks;

namespace WinAdmin.Infrastructure.EnvironmentChecks;

/// <summary>Фон: быстрые проверки через минуту после старта и далее раз в час.</summary>
public sealed class EnvironmentMonitor(IEnvironmentService environment, ILogger<EnvironmentMonitor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try { await environment.RunAsync(null, CheckDepth.Quick, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogWarning(ex, "Фоновая проверка окружения не выполнилась"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
