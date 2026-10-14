using System.Security.Cryptography.X509Certificates;
using WinAdmin.Core.Models;

namespace WinAdmin.Core.Abstractions;

/// <summary>Результат загрузки сертификата: либо сертификат (владеет вызывающий), либо понятная ошибка.</summary>
public sealed record CertificateLoadResult(X509Certificate2? Certificate, string? Error);

/// <summary>Проверка сертификата перед применением: сведения (если загрузился) и ошибки.</summary>
public sealed record CertificateValidation(CertificateInfo? Info, IReadOnlyList<string> Errors)
{
    public bool Ok => Errors.Count == 0;
}

/// <summary>Сертификаты HTTPS: загрузка по настройкам, список из хранилищ, самоподписанный.</summary>
public interface IHttpsCertificateLoader
{
    CertificateLoadResult Load(HttpsCertificateSettings settings);

    /// <summary>Загружает и проверяет (ключ, срок действия, назначение).</summary>
    CertificateValidation Validate(HttpsCertificateSettings settings);

    /// <summary>Сертификаты с закрытым ключом из LocalMachine\My и CurrentUser\My.</summary>
    IReadOnlyList<CertificateInfo> ListStoreCertificates();

    /// <summary>Выпускает самоподписанный сертификат в keys\https.pfx (заменяет прежний).</summary>
    CertificateInfo CreateSelfSigned(IReadOnlyList<string> dnsNames);

    /// <summary>Шифрует пароль PFX для network.json.</summary>
    string ProtectPfxPassword(string password);
}
