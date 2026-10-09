namespace WinAdmin.Core.Abstractions;

/// <summary>Свойства машины для требований модулей.</summary>
public interface IMachineInfo
{
    bool IsWindowsServer { get; }
    bool IsDomainJoined { get; }
}
