using WinAdmin.Core.Models;
using WinAdmin.Core.Network;

namespace WinAdmin.Api.Network;

/// <summary>Перенаправление с HTTP-порта на HTTPS (308) и HSTS для HTTPS-ответов.</summary>
public static class HttpsRedirect
{
    public const string HstsValue = "max-age=31536000";

    /// <summary>
    /// Адрес для 308 или null. Перенаправляется только запрос без TLS, пришедший на порт перенаправления
    /// (в TestServer LocalPort = 0 — не перенаправляется).
    /// </summary>
    public static string? Location(NetworkSettings settings, bool isHttps, int localPort, HostString host,
        PathString pathBase, PathString path, QueryString query)
    {
        if (isHttps || NetworkEndpoints.RedirectPort(settings) is not int redirect || localPort != redirect)
            return null;
        // HostString.Host — без порта, IPv6 в квадратных скобках.
        string name = string.IsNullOrEmpty(host.Host) ? "localhost" : host.Host;
        string authority = settings.Port == 443 ? name : $"{name}:{settings.Port}";
        return $"https://{authority}{pathBase}{path}{query}";
    }
}

/// <summary>Первым в конвейере: 308 с порта перенаправления, заголовок HSTS на HTTPS.</summary>
public sealed class HttpsRedirectMiddleware(RequestDelegate next, HttpsCertificateHolder holder)
{
    public Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        if (request.IsHttps)
        {
            // Без includeSubDomains: панель не отвечает за другие сайты домена.
            context.Response.Headers.StrictTransportSecurity = HttpsRedirect.HstsValue;
            return next(context);
        }

        string? location = HttpsRedirect.Location(holder.Settings, request.IsHttps, context.Connection.LocalPort,
            request.Host, request.PathBase, request.Path, request.QueryString);
        if (location is null)
            return next(context);

        context.Response.StatusCode = StatusCodes.Status308PermanentRedirect;
        context.Response.Headers.Location = location;
        return Task.CompletedTask;
    }
}
