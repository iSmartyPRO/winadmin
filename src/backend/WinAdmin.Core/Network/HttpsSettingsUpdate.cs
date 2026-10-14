using WinAdmin.Core.Models;

namespace WinAdmin.Core.Network;

/// <summary>Слияние запроса на изменение HTTPS с текущими настройками.</summary>
public static class HttpsSettingsUpdate
{
    /// <param name="protectPassword">Шифрует пароль PFX (открытым текстом в network.json он не попадает).</param>
    public static HttpsSettings? Merge(UpdateHttpsRequest? request, HttpsSettings? previous, Func<string, string> protectPassword)
    {
        if (request is null) return previous;

        var previousCert = previous?.Certificate;
        HttpsCertificateSettings? cert = request.Source switch
        {
            null => previousCert,
            HttpsCertificateSource.Store => new HttpsCertificateSettings(HttpsCertificateSource.Store,
                request.Thumbprint, request.StoreLocation),
            HttpsCertificateSource.Pfx => new HttpsCertificateSettings(HttpsCertificateSource.Pfx,
                Path: request.PfxPath,
                Password: request.PfxPassword is not null
                    ? protectPassword(request.PfxPassword)
                    : previousCert?.Source == HttpsCertificateSource.Pfx ? previousCert.Password : null),
            _ => new HttpsCertificateSettings(request.Source.Value),
        };

        return new HttpsSettings(request.Enabled, cert, request.HttpRedirectPort is > 0 ? request.HttpRedirectPort : null);
    }
}
