using System.Net;

namespace WinAdmin.Api.Auth;

/// <summary>Пароль домена — только по HTTPS или с этой машины. Заголовки прокси не учитываются.</summary>
public static class LoginTransport
{
    public const string RefusedMessage =
        "Вход учёткой домена по паролю доступен только по HTTPS или с этого компьютера; используйте вход Windows";

    public static bool PasswordAllowed(HttpContext context)
        => context.Request.IsHttps
           || context.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip);
}
