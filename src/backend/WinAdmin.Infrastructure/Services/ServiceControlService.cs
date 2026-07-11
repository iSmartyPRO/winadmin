using System.Management;
using System.Runtime.Versioning;
using System.ServiceProcess;
using Microsoft.Extensions.Logging;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;

namespace WinAdmin.Infrastructure.Services;

/// <summary>
/// Читает список служб через WMI (Win32_Service) и управляет ими через ServiceController.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ServiceControlService : IServiceControlService
{
    private static readonly TimeSpan ActionTimeout = TimeSpan.FromSeconds(30);
    private readonly ILogger<ServiceControlService> _logger;

    public ServiceControlService(ILogger<ServiceControlService> logger) => _logger = logger;

    public IReadOnlyList<ServiceInfo> GetServices()
    {
        var result = new List<ServiceInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, DisplayName, State, StartMode, StartName, AcceptStop, AcceptPause FROM Win32_Service");

            foreach (ManagementObject svc in searcher.Get().Cast<ManagementObject>())
            {
                result.Add(new ServiceInfo
                {
                    Name = svc["Name"]?.ToString() ?? "",
                    DisplayName = svc["DisplayName"]?.ToString() ?? svc["Name"]?.ToString() ?? "",
                    Status = svc["State"]?.ToString() ?? "Unknown",
                    StartType = svc["StartMode"]?.ToString() ?? "Unknown",
                    CanStop = svc["AcceptStop"] as bool? ?? false,
                    CanPauseAndContinue = svc["AcceptPause"] as bool? ?? false,
                    Account = svc["StartName"]?.ToString(),
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось перечислить службы");
        }
        return result.OrderBy(s => s.DisplayName).ToList();
    }

    public OperationResult Control(string serviceName, ServiceAction action)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            switch (action)
            {
                case ServiceAction.Start:
                    if (sc.Status is ServiceControllerStatus.Running)
                        return OperationResult.Ok($"Служба '{serviceName}' уже запущена");
                    sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, ActionTimeout);
                    return OperationResult.Ok($"Служба '{serviceName}' запущена");

                case ServiceAction.Stop:
                    if (sc.Status is ServiceControllerStatus.Stopped)
                        return OperationResult.Ok($"Служба '{serviceName}' уже остановлена");
                    if (!sc.CanStop)
                        return OperationResult.Fail($"Служба '{serviceName}' не допускает остановку");
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, ActionTimeout);
                    return OperationResult.Ok($"Служба '{serviceName}' остановлена");

                case ServiceAction.Restart:
                    if (sc.Status is not ServiceControllerStatus.Stopped)
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, ActionTimeout);
                    }
                    sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, ActionTimeout);
                    return OperationResult.Ok($"Служба '{serviceName}' перезапущена");

                default:
                    return OperationResult.Fail("Неизвестное действие");
            }
        }
        catch (System.ServiceProcess.TimeoutException)
        {
            return OperationResult.Fail($"Превышено время ожидания смены статуса службы '{serviceName}'");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Ошибка управления службой {Service}", serviceName);
            return OperationResult.Fail($"Не удалось выполнить действие: {ex.Message}");
        }
    }
}
