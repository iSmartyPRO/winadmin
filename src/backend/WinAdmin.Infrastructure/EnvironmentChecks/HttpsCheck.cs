using WinAdmin.Core.Abstractions;
using WinAdmin.Core.EnvironmentChecks;
using WinAdmin.Core.Models;
using WinAdmin.Core.Network;
using WinAdmin.Infrastructure.Network;

namespace WinAdmin.Infrastructure.EnvironmentChecks;

/// <summary>HTTPS панели: включён ли в режиме «Сеть», годен ли сертификат, совпадает ли имя.</summary>
public sealed class HttpsCheck(
    INetworkSettingsService network, IHttpsCertificateLoader certificates,
    TimeProvider? time = null, Func<string>? machineDnsName = null) : IEnvironmentCheck
{
    private const string Code = "platform.https";
    private const string Title = "HTTPS";
    private const string SettingsFix = "Настройки → Сеть → HTTPS";
    private static readonly TimeSpan ExpiryWarning = TimeSpan.FromDays(30);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Func<string> _machine = machineDnsName ?? MachineDnsName.Fqdn;

    public string ModuleId => EnvironmentService.Platform;

    public Task<IReadOnlyList<CheckResult>> RunAsync(CheckDepth depth, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<CheckResult>>([Check()]);

    private CheckResult Check()
    {
        var settings = network.Current;
        if (!settings.HttpsEnabled)
            return settings.Mode == NetworkMode.Network
                ? CheckResult.Warn(Code, Title,
                    "HTTPS выключен в режиме «Сеть»: пароли доменных учёток по сети недоступны (работают вход Windows и локальные учётки); трафик не шифруется",
                    $"Включите HTTPS: {SettingsFix} или WinAdmin.exe network https --enable")
                : CheckResult.Ok(Code, Title, "Панель доступна только с этого компьютера — HTTPS не требуется");

        var loaded = certificates.Load(settings.Https!.Certificate!);
        if (loaded.Certificate is null)
            return CheckResult.Fail(Code, Title, loaded.Error ?? "Сертификат не загружен",
                $"{SettingsFix}: выберите другой сертификат или WinAdmin.exe network https --disable");

        using var cert = loaded.Certificate;
        var now = _time.GetUtcNow();
        var problems = HttpsCertificates.Problems(cert, now);
        if (problems.Count > 0)
            return CheckResult.Fail(Code, Title, string.Join(" ", problems), $"{SettingsFix}: выберите действующий сертификат с закрытым ключом");

        var warnings = new List<string>();
        var notAfter = new DateTimeOffset(cert.NotAfter);
        if (notAfter - now < ExpiryWarning)
            warnings.Add($"сертификат истекает {notAfter.ToLocalTime():dd.MM.yyyy}");
        string machine = _machine();
        if (!HttpsCertificates.CoversHost(cert, machine))
            warnings.Add($"DNS-имя компьютера {machine} не указано в сертификате (SAN: {string.Join(", ", HttpsCertificates.DnsNames(cert))}) — браузер покажет ошибку имени");

        return warnings.Count > 0
            ? CheckResult.Warn(Code, Title, $"{cert.Subject}: {string.Join("; ", warnings)}",
                $"Выпустите новый сертификат и выберите его: {SettingsFix}")
            : CheckResult.Ok(Code, Title, $"{cert.Subject}, действует до {notAfter.ToLocalTime():dd.MM.yyyy}");
    }
}
