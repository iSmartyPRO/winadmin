using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging.Abstractions;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Network;
using WinAdmin.Infrastructure.Hardening;
using WinAdmin.Infrastructure.Secrets;

namespace WinAdmin.Infrastructure.Network;

/// <summary>Загрузка сертификата HTTPS по настройкам из network.json.</summary>
public sealed class HttpsCertificateLoader(
    ICertificateStore store, ISecretProtector protector, string keysDirectory, TimeProvider? time = null) : IHttpsCertificateLoader
{
    public const string PfxPasswordPurpose = "network.https.pfx";
    public const string SelfSignedPasswordPurpose = "network.https.selfsigned";
    public const string SelfSignedFileName = "https.pfx";

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public string SelfSignedPath => Path.Combine(keysDirectory, SelfSignedFileName);
    private string SelfSignedPasswordPath => SelfSignedPath + ".password";

    public CertificateLoadResult Load(HttpsCertificateSettings settings) => settings.Source switch
    {
        HttpsCertificateSource.Store => FromStore(settings),
        HttpsCertificateSource.Pfx => FromPfx(settings.Path, settings.Password, PfxPasswordPurpose,
            $"Файл PFX не найден: {settings.Path}"),
        HttpsCertificateSource.SelfSigned => LoadSelfSigned(),
        _ => new(null, "Неизвестный источник сертификата."),
    };

    /// <summary>
    /// Общая блокировка для каталога ключей: генерация меняет пару «PFX + пароль» двумя переименованиями,
    /// читатель не должен увидеть новый PFX со старым паролем, а два выпуска — перемешать файлы.
    /// Ключ — каталог, чтобы разные экземпляры загрузчика в одном процессе тоже не мешали друг другу.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> SelfSignedLocks =
        new(StringComparer.OrdinalIgnoreCase);

    private object SelfSignedLock => SelfSignedLocks.GetOrAdd(Path.GetFullPath(keysDirectory), _ => new object());

    private CertificateLoadResult LoadSelfSigned()
    {
        lock (SelfSignedLock)
            return FromPfx(SelfSignedPath, ReadSelfSignedPassword(), SelfSignedPasswordPurpose,
                "Самоподписанный сертификат не создан — создайте его (Настройки → Сеть → HTTPS или WinAdmin.exe network https --self-signed).");
    }

    public CertificateValidation Validate(HttpsCertificateSettings settings)
    {
        var loaded = Load(settings);
        if (loaded.Certificate is null)
            return new(null, [loaded.Error ?? "Сертификат не загружен."]);
        using var cert = loaded.Certificate;
        var location = settings.Source == HttpsCertificateSource.Store ? settings.StoreLocation ?? CertificateStoreLocation.LocalMachine : (CertificateStoreLocation?)null;
        return new(HttpsCertificates.Describe(cert, location), HttpsCertificates.Problems(cert, _time.GetUtcNow()));
    }

    public IReadOnlyList<CertificateInfo> ListStoreCertificates()
        => store.List()
            .Where(i => i.Certificate.HasPrivateKey)
            .Select(i => HttpsCertificates.Describe(i.Certificate, i.Location))
            .OrderBy(i => i.StoreLocation)
            .ThenByDescending(i => i.NotAfter)
            .ToList();

    public CertificateInfo CreateSelfSigned(IReadOnlyList<string> dnsNames)
    {
        using var cert = HttpsCertificates.CreateSelfSigned(dnsNames, _time.GetUtcNow());
        string password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        byte[] pfx = cert.Export(X509ContentType.Pfx, password);
        string protectedPassword = protector.Protect(password, SelfSignedPasswordPurpose);

        lock (SelfSignedLock)
        {
            Directory.CreateDirectory(keysDirectory);
            // Уникальные имена: параллельный выпуск (в том числе из другого процесса — CLI) не перепишет наши файлы.
            string suffix = "." + Guid.NewGuid().ToString("N") + ".tmp";
            string pfxTmp = SelfSignedPath + suffix, passwordTmp = SelfSignedPasswordPath + suffix;
            try
            {
                File.WriteAllBytes(pfxTmp, pfx);
                File.WriteAllText(passwordTmp, protectedPassword);
                foreach (string tmp in new[] { pfxTmp, passwordTmp })
                    InstallationHardening.ProtectFile(tmp, MasterKeyStore.KeyFileRules(), NullLogger.Instance);
                File.Move(pfxTmp, SelfSignedPath, overwrite: true);
                File.Move(passwordTmp, SelfSignedPasswordPath, overwrite: true);
            }
            finally
            {
                foreach (string tmp in new[] { pfxTmp, passwordTmp })
                    try { File.Delete(tmp); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        return HttpsCertificates.Describe(cert);
    }

    public string ProtectPfxPassword(string password) => protector.Protect(password, PfxPasswordPurpose);

    private CertificateLoadResult FromStore(HttpsCertificateSettings settings)
    {
        var location = settings.StoreLocation ?? CertificateStoreLocation.LocalMachine;
        string thumbprint = NetworkSettingsValidator.NormalizeThumbprint(settings.Thumbprint);
        var cert = store.Find(location, thumbprint);
        return cert is null
            ? new(null, $"Сертификат с отпечатком {thumbprint} не найден в хранилище {location}\\My.")
            : new(cert, null);
    }

    private string? ReadSelfSignedPassword()
        => File.Exists(SelfSignedPasswordPath) ? File.ReadAllText(SelfSignedPasswordPath).Trim() : null;

    private CertificateLoadResult FromPfx(string? path, string? protectedPassword, string purpose, string notFound)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new(null, notFound);
        // До любого обращения к файлу: по UNC-пути LocalSystem аутентифицировался бы на чужом сервере.
        if (!NetworkSettingsValidator.IsLocalDrivePath(path))
            return new(null, $"Файл PFX должен лежать на локальном диске сервера (путь вида C:\\certs\\panel.pfx): {path}");
        if (!File.Exists(path))
            return new(null, notFound);

        string? password = null;
        if (!string.IsNullOrEmpty(protectedPassword))
        {
            try
            {
                password = protector.Unprotect(protectedPassword, purpose);
            }
            catch (SecretUnavailableException ex)
            {
                return new(null, $"Не удалось расшифровать пароль PFX: {ex.Message} Укажите пароль заново.");
            }
        }

        try
        {
            // Флаги по умолчанию: ключ «в памяти» (EphemeralKeySet) SChannel не принимает.
            return new(X509CertificateLoader.LoadPkcs12FromFile(path, password), null);
        }
        catch (CryptographicException)
        {
            return new(null, $"Не удалось открыть {path}: неверный пароль или повреждённый файл PFX.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(null, $"Не удалось прочитать {path}: {ex.Message}");
        }
    }
}
