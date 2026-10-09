using WinAdmin.Core.Models;
using WinAdmin.Core.Network;

namespace WinAdmin.Tests;

public sealed class NetworkSettingsValidatorTests
{
    private static NetworkSettings S(NetworkMode mode, int port, params string[] allow) => new(mode, port, allow);

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(65535, true)]
    [InlineData(65536, false)]
    public void Validates_port_range(int port, bool valid)
    {
        var (_, errors) = NetworkSettingsValidator.Normalize(S(NetworkMode.Local, port));
        Assert.Equal(valid, errors.Count == 0);
    }

    [Theory]
    [InlineData("10.77.77.0/24")]
    [InlineData("192.168.88.5")]
    public void Accepts_addresses_and_cidr(string item)
    {
        var (_, errors) = NetworkSettingsValidator.Normalize(S(NetworkMode.Network, 8080, item));
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("10")]              // IPAddress.TryParse принимает как 0.0.0.10
    [InlineData("abc")]
    [InlineData("10.0.0.0/33")]
    [InlineData("10.0.0.0/x")]
    [InlineData("0.0.0.0/0")]       // «любой адрес» запрещён
    [InlineData("::/0")]
    [InlineData("2001:db8::/129")]
    [InlineData("010.0.0.1")]       // восьмеричная запись → 8.0.0.1, неочевидно
    [InlineData("0x0a.0.0.1")]
    [InlineData("10.0.0.01")]
    [InlineData("fe80::1%12")]      // scope id — netsh не примет
    [InlineData("[::1]:80")]
    [InlineData("fe80::1")]         // IPv6: режим «Сеть» слушает только IPv4 (0.0.0.0)
    [InlineData("2001:db8::/32")]
    public void Rejects_garbage_and_any(string item)
    {
        var (_, errors) = NetworkSettingsValidator.Normalize(S(NetworkMode.Network, 8080, item));
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Network_mode_requires_allow_list()
    {
        var (_, errors) = NetworkSettingsValidator.Normalize(S(NetworkMode.Network, 8080));
        Assert.Single(errors);
    }

    [Fact]
    public void Local_mode_keeps_allow_list_without_requiring_it()
    {
        var (settings, errors) = NetworkSettingsValidator.Normalize(S(NetworkMode.Local, 8080, "10.0.0.0/8"));
        Assert.Empty(errors);
        Assert.Equal(new[] { "10.0.0.0/8" }, settings.Allow);
    }

    [Fact]
    public void Trims_drops_empty_and_deduplicates()
    {
        var (settings, errors) = NetworkSettingsValidator.Normalize(
            S(NetworkMode.Network, 8080, " 10.0.0.0/8 ", "", "10.0.0.0/8", "192.168.1.1"));
        Assert.Empty(errors);
        Assert.Equal(new[] { "10.0.0.0/8", "192.168.1.1" }, settings.Allow);
    }

    [Fact]
    public void Null_allow_is_treated_as_empty()
    {
        var (settings, errors) = NetworkSettingsValidator.Normalize(new NetworkSettings(NetworkMode.Local, 8080, null!));
        Assert.Empty(errors);
        Assert.Empty(settings.Allow);
    }

    [Fact]
    public void Rejects_undefined_mode()
    {
        var (_, errors) = NetworkSettingsValidator.Normalize(S((NetworkMode)2, 8080, "10.0.0.0/8"));
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Equivalence_compares_allow_by_content()
    {
        var a = S(NetworkMode.Network, 8080, "10.0.0.0/8");
        var b = S(NetworkMode.Network, 8080, "10.0.0.0/8");
        Assert.True(a.IsEquivalentTo(b));
        Assert.False(a.IsEquivalentTo(b with { Port = 9090 }));
    }
}
