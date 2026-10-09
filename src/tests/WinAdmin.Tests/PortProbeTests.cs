using System.Net;
using System.Net.Sockets;
using WinAdmin.Core.Models;
using WinAdmin.Infrastructure.Network;

namespace WinAdmin.Tests;

public sealed class PortProbeTests
{
    [Fact]
    public void Detects_listening_loopback_port()
    {
        var probe = new SystemPortProbe();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            Assert.True(probe.IsInUse(port));
            Assert.True(probe.IsListening(new NetworkSettings(NetworkMode.Local, port, [])));
            Assert.False(probe.IsListening(new NetworkSettings(NetworkMode.Network, port, ["10.0.0.0/8"])));
        }
        finally
        {
            listener.Stop();
        }
        Assert.False(probe.IsInUse(port));
    }
}
