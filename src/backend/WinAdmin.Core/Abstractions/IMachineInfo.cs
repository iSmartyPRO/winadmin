namespace WinAdmin.Core.Abstractions;

/// <summary>Свойства машины для требований модулей.</summary>
public interface IMachineInfo
{
    bool IsWindowsServer { get; }
    bool IsDomainJoined { get; }

    /// <summary>DNS-имя домена (Win32_ComputerSystem.Domain), если машина в домене.</summary>
    string? DomainName { get; }
}
