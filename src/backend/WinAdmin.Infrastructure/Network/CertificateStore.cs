using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using WinAdmin.Core.Models;

namespace WinAdmin.Infrastructure.Network;

/// <summary>Хранилище сертификатов «Личные» (My).</summary>
public interface ICertificateStore
{
    X509Certificate2? Find(CertificateStoreLocation location, string thumbprint);

    IReadOnlyList<(CertificateStoreLocation Location, X509Certificate2 Certificate)> List();
}

/// <summary>Хранилища Windows LocalMachine\My и CurrentUser\My (только чтение).</summary>
public sealed class SystemCertificateStore : ICertificateStore
{
    public X509Certificate2? Find(CertificateStoreLocation location, string thumbprint)
    {
        try
        {
            using var store = Open(location);
            var found = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
            return found.Count > 0 ? found[0] : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public IReadOnlyList<(CertificateStoreLocation Location, X509Certificate2 Certificate)> List()
    {
        var result = new List<(CertificateStoreLocation, X509Certificate2)>();
        foreach (var location in Enum.GetValues<CertificateStoreLocation>())
        {
            try
            {
                using var store = Open(location);
                foreach (var cert in store.Certificates)
                    result.Add((location, cert));
            }
            catch (CryptographicException)
            {
                // Хранилище недоступно (например, профиль не загружен) — показываем остальные.
            }
        }
        return result;
    }

    private static X509Store Open(CertificateStoreLocation location)
    {
        var store = new X509Store(StoreName.My,
            location == CertificateStoreLocation.CurrentUser ? StoreLocation.CurrentUser : StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        return store;
    }
}
