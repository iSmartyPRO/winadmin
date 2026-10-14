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

    [Fact]
    public void Https_with_redirect_requires_both_ports()
    {
        var probe = new SystemPortProbe();
        var main = new TcpListener(IPAddress.Loopback, 0);
        main.Start();
        int port = ((IPEndPoint)main.LocalEndpoint).Port;
        var redirect = new TcpListener(IPAddress.Loopback, 0);
        redirect.Start();
        int redirectPort = ((IPEndPoint)redirect.LocalEndpoint).Port;
        try
        {
            var https = new HttpsSettings(true, new HttpsCertificateSettings(HttpsCertificateSource.SelfSigned), redirectPort);
            var settings = new NetworkSettings(NetworkMode.Local, port, [], https);
            Assert.True(probe.IsListening(settings));
            redirect.Stop();
            Assert.False(probe.IsListening(settings));
        }
        finally
        {
            main.Stop();
            redirect.Stop();
        }
    }
}
