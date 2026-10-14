using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace WinAdmin.Infrastructure.Network;

/// <summary>Проверка, что порт действительно отвечает по TLS (для отката сетевых настроек).</summary>
public interface ITlsProbe
{
    /// <summary>Отпечаток сертификата сервера на 127.0.0.1:port или null, если рукопожатие не удалось.</summary>
    Task<string?> HandshakeAsync(int port, CancellationToken ct);
}

public sealed class TlsProbe : ITlsProbe
{
    public async Task<string?> HandshakeAsync(int port, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
            // Сертификат здесь не проверяется: самоподписанный тоже годится, важен сам факт рукопожатия.
            await using var tls = new SslStream(tcp.GetStream(), false, (_, _, _, _) => true);
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "localhost",
                ApplicationProtocols = [SslApplicationProtocol.Http11],
            }, timeout.Token);
            return tls.RemoteCertificate is { } cert ? new X509Certificate2(cert).Thumbprint : null;
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException
                                       or System.Security.Authentication.AuthenticationException)
        {
            return null;
        }
    }
}
