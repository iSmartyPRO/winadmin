namespace WinAdmin.Core.Abstractions;

/// <summary>Состояние службы Windows: нет такой службы, остановлена, работает (или переходит между состояниями), не удалось узнать.</summary>
public enum WindowsServiceState { NotInstalled, Stopped, Running, StartPending, StopPending, Paused, Unknown }

/// <summary>Узнать состояние службы Windows (для CLI: не менять данные под работающей службой).</summary>
public interface IWindowsServiceStatus
{
    WindowsServiceState GetState(string serviceName);
}
