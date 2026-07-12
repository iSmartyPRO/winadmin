using WinAdmin.Core.Models;

namespace WinAdmin.Core.Abstractions;

public interface ISystemInfoService
{
    SystemInfo GetSystemInfo();
    SystemMetrics GetMetrics();
}

public interface IDiskService
{
    IReadOnlyList<PhysicalDisk> GetDisks();
}

public interface IServiceControlService
{
    IReadOnlyList<ServiceInfo> GetServices();
    OperationResult Control(string serviceName, ServiceAction action);
}

public interface IProcessService
{
    IReadOnlyList<ProcessInfo> GetProcesses();
    OperationResult Kill(int pid);
}

public interface IPrinterService
{
    IReadOnlyList<PrinterInfo> GetPrinters();
    OperationResult Control(string printerName, PrinterAction action);
}

public interface IPowerService
{
    OperationResult Reboot(PowerRequest request);
    OperationResult Shutdown(PowerRequest request);
    OperationResult CancelPending();
}

public interface IEventLogService
{
    IReadOnlyList<string> GetLogNames();
    EventLogQueryResult Query(EventLogQueryRequest request);
}

public interface ISoftwareCatalogService
{
    IReadOnlyList<InstalledApp> GetApplications();
    IReadOnlyList<InstalledUpdate> GetUpdates();
    InstalledApp? FindApplication(string id);
    InstalledUpdate? FindUpdate(string id);
}

public interface ISoftwareJobService
{
    SoftwareJob StartUninstallApp(string appId);
    SoftwareJob StartUninstallUpdate(string updateId);
    SoftwareJob StartRollbackUpdate(string updateId);
    SoftwareJob? GetJob(string jobId);
    SoftwareJob? GetActiveJob();
}
