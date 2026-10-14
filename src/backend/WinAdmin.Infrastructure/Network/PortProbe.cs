using System.Net;
using System.Net.NetworkInformation;
using WinAdmin.Core.Models;
using WinAdmin.Core.Network;

namespace WinAdmin.Infrastructure.Network;

/// <summary>Проверка занятости портов (без открытия сокетов — без запроса брандмауэра).</summary>
public interface IPortProbe
{
    /// <summary>Порт слушает кто-либо на любом адресе.</summary>
    bool IsInUse(int port);

    /// <summary>Endpoint из настроек (и порт перенаправления HTTP, если задан) сейчас слушается.</summary>
    bool IsListening(NetworkSettings settings);
}

public sealed class SystemPortProbe : IPortProbe
{
    public bool IsInUse(int port) => Listeners().Any(e => e.Port == port);

    public bool IsListening(NetworkSettings settings)
    {
        var address = NetworkEndpoints.BindAddress(settings);
        var listeners = Listeners();
        return new[] { settings.Port, NetworkEndpoints.RedirectPort(settings) }.OfType<int>()
            .All(port => listeners.Any(e => e.Port == port && e.Address.Equals(address)));
    }

    private static IPEndPoint[] Listeners() => IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
}
