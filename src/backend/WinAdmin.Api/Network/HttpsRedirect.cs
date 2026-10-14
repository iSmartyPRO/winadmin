using WinAdmin.Core.Models;
using WinAdmin.Core.Network;

namespace WinAdmin.Api.Network;

/// <summary>Перенаправление с HTTP-порта на HTTPS (308) и HSTS для HTTPS-ответов на порту 443.</summary>
public static class HttpsRedirect
{
    /// <summary>Сутки: после выключения HTTPS браузеры быстро «забудут» принудительный https://.</summary>
    public const string HstsValue = "max-age=86400";

    /// <summary>
    /// Значение заголовка HSTS или null. HSTS действует на всё имя хоста, а не на порт (RFC 6797 §8.3):
    /// при HTTPS на нестандартном порту браузер переписал бы http://srv:8080 (порт перенаправления) и
    /// другие HTTP-сайты сервера (IIS:80) на https:// — поэтому только для основного порта 443.
    /// Без includeSubDomains: панель не отвечает за другие сайты домена.
    /// </summary>
    public static string? Hsts(NetworkSettings settings, bool isHttps)
        => isHttps && settings.HttpsEnabled && settings.Port == 443 ? HstsValue : null;

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

/// <summary>Первым в конвейере: 308 с порта перенаправления, заголовок HSTS на HTTPS (только порт 443).</summary>
public sealed class HttpsRedirectMiddleware(RequestDelegate next, HttpsCertificateHolder holder)
{
    public Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        if (request.IsHttps)
        {
            if (HttpsRedirect.Hsts(holder.Settings, isHttps: true) is string hsts)
                context.Response.Headers.StrictTransportSecurity = hsts;
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
