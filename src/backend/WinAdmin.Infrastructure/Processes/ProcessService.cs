using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;

namespace WinAdmin.Infrastructure.Processes;

/// <summary>Перечисляет процессы и завершает их по PID.</summary>
[SupportedOSPlatform("windows")]
public sealed class ProcessService : IProcessService
{
    private readonly ILogger<ProcessService> _logger;

    public ProcessService(ILogger<ProcessService> logger) => _logger = logger;

    public IReadOnlyList<ProcessInfo> GetProcesses()
    {
        var result = new List<ProcessInfo>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                string? windowTitle = null;
                bool hasWindow = false;
                try
                {
                    hasWindow = p.MainWindowHandle != IntPtr.Zero;
                    if (hasWindow)
                        windowTitle = p.MainWindowTitle;
                }
                catch { /* у некоторых процессов нет доступа к окну */ }

                DateTimeOffset? start = null;
                try { start = p.StartTime; } catch { /* доступ запрещён для системных процессов */ }

                result.Add(new ProcessInfo
                {
                    Pid = p.Id,
                    Name = p.ProcessName,
                    MainWindowTitle = string.IsNullOrWhiteSpace(windowTitle) ? null : windowTitle,
                    WorkingSetBytes = p.WorkingSet64,
                    ThreadCount = p.Threads.Count,
                    StartTime = start,
                    HasWindow = hasWindow,
                });
            }
            catch (Exception ex)
            {
                _logger.LogTrace(ex, "Пропущен процесс {Pid}", p.Id);
            }
            finally
            {
                p.Dispose();
            }
        }
        return result.OrderByDescending(p => p.WorkingSetBytes).ToList();
    }

    public OperationResult Kill(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            string name = p.ProcessName;
            p.Kill(entireProcessTree: true);
            return OperationResult.Ok($"Процесс {name} (PID {pid}) завершён");
        }
        catch (ArgumentException)
        {
            return OperationResult.Fail($"Процесс с PID {pid} не найден");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось завершить процесс {Pid}", pid);
            return OperationResult.Fail($"Не удалось завершить процесс {pid}: {ex.Message}");
        }
    }
}
