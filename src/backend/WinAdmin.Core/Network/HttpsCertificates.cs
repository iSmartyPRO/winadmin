using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using WinAdmin.Core.Models;

namespace WinAdmin.Core.Network;

/// <summary>Проверка, описание и выпуск сертификатов HTTPS (чистые функции).</summary>
public static class HttpsCertificates
{
    public const string ServerAuthOid = "1.3.6.1.5.5.7.3.1";
    private const string AnyExtendedKeyUsageOid = "2.5.29.37.0";

    /// <summary>DNS-имена из расширения SAN.</summary>
    public static IReadOnlyList<string> DnsNames(X509Certificate2 cert)
        => cert.Extensions.OfType<X509SubjectAlternativeNameExtension>()
            .SelectMany(e => e.EnumerateDnsNames())
            .ToList();

    public static CertificateInfo Describe(X509Certificate2 cert, CertificateStoreLocation? location = null)
        => new(cert.Thumbprint, cert.Subject, cert.Issuer, DnsNames(cert),
            new DateTimeOffset(cert.NotBefore), new DateTimeOffset(cert.NotAfter), cert.HasPrivateKey, location);

    /// <summary>Почему сертификат не годится для HTTPS (пусто — годится).</summary>
    public static IReadOnlyList<string> Problems(X509Certificate2 cert, DateTimeOffset now)
    {
        var problems = new List<string>();
        if (!cert.HasPrivateKey)
            problems.Add($"У сертификата {cert.Subject} нет закрытого ключа — HTTPS с ним работать не будет.");
        var notAfter = new DateTimeOffset(cert.NotAfter);
        var notBefore = new DateTimeOffset(cert.NotBefore);
        if (notAfter <= now)
            problems.Add($"Срок действия сертификата {cert.Subject} истёк {notAfter.ToLocalTime():dd.MM.yyyy}.");
        if (notBefore > now)
            problems.Add($"Сертификат {cert.Subject} ещё не действует (начало действия {notBefore.ToLocalTime():dd.MM.yyyy HH:mm}).");

        var usages = cert.Extensions.OfType<X509EnhancedKeyUsageExtension>().ToList();
        if (usages.Count > 0 && !usages.Any(u => u.EnhancedKeyUsages.Cast<Oid>()
                .Any(o => o.Value is ServerAuthOid or AnyExtendedKeyUsageOid)))
            problems.Add($"Сертификат {cert.Subject} не предназначен для проверки подлинности сервера (нет назначения «Server Authentication»).");
        return problems;
    }

    /// <summary>Подходит ли сертификат для имени хоста (SAN, «*.» — ровно один уровень).</summary>
    public static bool CoversHost(X509Certificate2 cert, string host)
        => DnsNames(cert).Any(name => NameMatches(name, host));

    private static bool NameMatches(string pattern, string host)
    {
        if (string.Equals(pattern, host, StringComparison.OrdinalIgnoreCase)) return true;
        if (!pattern.StartsWith("*.", StringComparison.Ordinal)) return false;
        int dot = host.IndexOf('.');
        return dot > 0 && string.Equals(pattern[1..], host[dot..], StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Имена для самоподписанного: FQDN, короткое имя, localhost и дополнительные (без повторов).</summary>
    public static IReadOnlyList<string> SelfSignedDnsNames(string machineName, string? domainName, IEnumerable<string> extra)
    {
        var names = new List<string>();
        void Add(string name)
        {
            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase)) names.Add(name);
        }

        if (!string.IsNullOrWhiteSpace(domainName)) Add($"{machineName}.{domainName.Trim('.', ' ')}");
        Add(machineName);
        Add("localhost");
        foreach (string raw in extra)
        {
            string name = raw?.Trim() ?? "";
            if (name.Length == 0) continue;
            if (Uri.CheckHostName(name) != UriHostNameType.Dns)
                throw new ArgumentException($"«{name}» — не DNS-имя (например panel.corp.local).");
            Add(name);
        }
        return names;
    }

    /// <summary>Самоподписанный сертификат: RSA 2048, SHA-256, SAN, Server Authentication, 2 года.</summary>
    public static X509Certificate2 CreateSelfSigned(IReadOnlyList<string> dnsNames, DateTimeOffset now)
    {
        if (dnsNames.Count == 0) throw new ArgumentException("Нужно хотя бы одно DNS-имя.", nameof(dnsNames));

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={dnsNames[0]}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        foreach (string name in dnsNames) san.AddDnsName(name);
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(ServerAuthOid)], critical: false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

        // Начало — сутки назад: у клиентов с отстающими часами сертификат не будет «ещё не действующим».
        using var created = request.CreateSelfSigned(now.AddDays(-1), now.AddYears(2));
        // Ключ в памяти (ephemeral) SChannel не принимает — перезагружаем через PFX.
        return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable);
    }
}
