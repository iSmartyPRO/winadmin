using WinAdmin.Core.Abstractions;

namespace WinAdmin.Tests.Fakes;

public sealed class FakeWindowsServiceStatus : IWindowsServiceStatus
{
    public WindowsServiceState State { get; set; } = WindowsServiceState.NotInstalled;
    public List<string> Queried { get; } = [];

    public WindowsServiceState GetState(string serviceName)
    {
        Queried.Add(serviceName);
        return State;
    }
}
