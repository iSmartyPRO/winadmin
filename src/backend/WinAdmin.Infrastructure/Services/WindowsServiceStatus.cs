using System.ComponentModel;
using System.Runtime.Versioning;
using System.ServiceProcess;
using WinAdmin.Core.Abstractions;

namespace WinAdmin.Infrastructure.Services;

/// <summary>Состояние службы через ServiceController; нет службы — NotInstalled, нет прав или иной сбой — Unknown.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsServiceStatus : IWindowsServiceStatus
{
    private const int ErrorServiceDoesNotExist = 1060;

    public WindowsServiceState GetState(string serviceName)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            return sc.Status switch
            {
                ServiceControllerStatus.Stopped => WindowsServiceState.Stopped,
                ServiceControllerStatus.Running => WindowsServiceState.Running,
                ServiceControllerStatus.StartPending or ServiceControllerStatus.ContinuePending => WindowsServiceState.StartPending,
                ServiceControllerStatus.StopPending => WindowsServiceState.StopPending,
                ServiceControllerStatus.Paused or ServiceControllerStatus.PausePending => WindowsServiceState.Paused,
                _ => WindowsServiceState.Unknown,
            };
        }
        catch (InvalidOperationException ex) when (ex.InnerException is Win32Exception { NativeErrorCode: ErrorServiceDoesNotExist })
        {
            return WindowsServiceState.NotInstalled;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return WindowsServiceState.Unknown;
        }
    }
}
