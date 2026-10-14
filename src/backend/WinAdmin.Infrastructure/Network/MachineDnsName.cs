using System.Net.NetworkInformation;

namespace WinAdmin.Infrastructure.Network;

/// <summary>DNS-имя этого компьютера (короткое имя и DNS-суффикс домена).</summary>
public static class MachineDnsName
{
    public static (string Host, string? Domain) Current()
    {
        try
        {
            var props = IPGlobalProperties.GetIPGlobalProperties();
            string host = string.IsNullOrWhiteSpace(props.HostName) ? Environment.MachineName : props.HostName;
            return (host, string.IsNullOrWhiteSpace(props.DomainName) ? null : props.DomainName);
        }
        catch (NetworkInformationException)
        {
            return (Environment.MachineName, null);
        }
    }

    /// <summary>FQDN, если компьютер в домене, иначе короткое имя.</summary>
    public static string Fqdn()
    {
        var (host, domain) = Current();
        return domain is null ? host : $"{host}.{domain}";
    }
}
