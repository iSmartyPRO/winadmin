using WinAdmin.Infrastructure.Network;

namespace WinAdmin.Api.Network;

public static class NetworkKestrelExtensions
{
    /// <summary>
    /// Kestrel:Endpoints из network.json (перепривязка на лету) и сертификат HTTPS через
    /// ServerCertificateSelector: пароль PFX не попадает в IConfiguration, а смена сертификата
    /// без смены адреса не требует остановки слушателя.
    /// </summary>
    public static NetworkConfigurationSource UseWinAdminNetwork(this WebApplicationBuilder builder,
        NetworkSettingsStore store, HttpsCertificateHolder holder)
    {
        builder.Services.AddSingleton(holder);
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.ConfigureHttpsDefaults(https =>
            https.ServerCertificateSelector = (_, _) => holder.Certificate));
        var source = new NetworkConfigurationSource(store, holder);
        builder.Configuration.Sources.Add(source);
        return source;
    }

    /// <summary>308 с HTTP-порта перенаправления и HSTS — первым в конвейере.</summary>
    public static IApplicationBuilder UseWinAdminHttps(this IApplicationBuilder app)
        => app.UseMiddleware<HttpsRedirectMiddleware>();
}
