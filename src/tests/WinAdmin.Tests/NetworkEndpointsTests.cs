using System.Net;
using WinAdmin.Core.Models;
using WinAdmin.Core.Network;

namespace WinAdmin.Tests;

public sealed class NetworkEndpointsTests
{
    [Fact]
    public void Local_listens_on_loopback()
    {
        var s = new NetworkSettings(NetworkMode.Local, 9090, []);
        Assert.Equal("http://127.0.0.1:9090", NetworkEndpoints.ListenUrl(s));
        Assert.Equal(IPAddress.Loopback, NetworkEndpoints.BindAddress(s));
        Assert.Equal("http://127.0.0.1:9090", NetworkEndpoints.PanelUrl(s, "server01"));
    }

    [Fact]
    public void Network_listens_on_all_interfaces_and_uses_request_host()
    {
        var s = new NetworkSettings(NetworkMode.Network, 9090, ["10.0.0.0/8"]);
        Assert.Equal("http://0.0.0.0:9090", NetworkEndpoints.ListenUrl(s));
        Assert.Equal(IPAddress.Any, NetworkEndpoints.BindAddress(s));
        Assert.Equal("http://server01:9090", NetworkEndpoints.PanelUrl(s, "server01"));
        Assert.Equal($"http://{Environment.MachineName}:9090", NetworkEndpoints.PanelUrl(s, null));
    }

    [Theory]
    [InlineData("http://0.0.0.0:8080", 8080)]
    [InlineData("http://+:9090", 9090)]
    [InlineData("http://[::]:7070", 7070)]
    [InlineData("http://localhost:5000/;https://localhost:5001", 5000)]
    [InlineData("http://localhost", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Extracts_port_from_urls(string? urls, int? expected)
        => Assert.Equal(expected, NetworkEndpoints.PortFromUrls(urls));
}
