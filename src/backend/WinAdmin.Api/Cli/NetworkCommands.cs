using System.CommandLine;
using System.CommandLine.Invocation;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Network;
using WinAdmin.Infrastructure.Network;

namespace WinAdmin.Api.Cli;

public static class NetworkCommands
{
    public static void Register(Command networkCommand, IServiceProvider provider, TextWriter? output = null, TextWriter? error = null)
    {
        var o = output ?? Console.Out;
        var e = error ?? Console.Error;
        networkCommand.AddCommand(ShowCommand(provider, o));
        networkCommand.AddCommand(SetCommand(provider, o, e));
        networkCommand.AddCommand(HttpsCommand(provider, o, e));
    }

    // ── show ─────────────────────────────────────────────────────
    private static Command ShowCommand(IServiceProvider provider, TextWriter output)
    {
        var cmd = new Command("show", "Показать сетевые настройки панели");
        cmd.SetHandler(() =>
        {
            var store = provider.GetRequiredService<NetworkSettingsStore>();
            var s = store.ReadOrDefault(out var problem);
            output.WriteLine($"Файл:     {store.FilePath}");
            if (problem is not null)
                output.WriteLine($"Ошибка:   {problem} (используются значения по умолчанию)");
            output.WriteLine($"Режим:    {(s.Mode == NetworkMode.Local ? "local (только этот компьютер)" : "network (сеть)")}");
            output.WriteLine($"Порт:     {s.Port}");
            output.WriteLine($"Подсети:  {(s.Allow.Count == 0 ? "—" : string.Join(", ", s.Allow))}");
            output.WriteLine($"HTTPS:    {(s.HttpsEnabled ? "включён" : "выключен")}");
            output.WriteLine($"Адрес:    {NetworkEndpoints.PanelUrl(s, null)}");
        });
        return cmd;
    }

    // ── set ──────────────────────────────────────────────────────
    private static Command SetCommand(IServiceProvider provider, TextWriter output, TextWriter error)
    {
        var modeOpt = new Option<string?>("--mode", "local — только этот компьютер; network — сеть (нужен --allow)");
        var portOpt = new Option<int?>("--port", "Порт панели (1–65535)");
        var allowOpt = new Option<string?>("--allow", "Разрешённые адреса/подсети через запятую, например 10.77.77.0/24,192.168.88.5");
        var cmd = new Command("set", "Изменить сетевые настройки (нужны права администратора)") { modeOpt, portOpt, allowOpt };

        cmd.SetHandler((InvocationContext ctx) =>
        {
            var network = provider.GetRequiredService<INetworkSettingsService>();
            var current = network.Current;

            string? modeRaw = ctx.ParseResult.GetValueForOption(modeOpt);
            var mode = current.Mode;
            if (modeRaw is not null)
            {
                switch (modeRaw.Trim().ToLowerInvariant())
                {
                    case "local": mode = NetworkMode.Local; break;
                    case "network": mode = NetworkMode.Network; break;
                    default:
                        error.WriteLine($"Неизвестный режим «{modeRaw}». Допустимо: local, network.");
                        ctx.ExitCode = 1;
                        return;
                }
            }

            int port = ctx.ParseResult.GetValueForOption(portOpt) ?? current.Port;
            string? allowRaw = ctx.ParseResult.GetValueForOption(allowOpt);
            IReadOnlyList<string> allow = allowRaw is null
                ? current.Allow
                : allowRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            ctx.ExitCode = CheckAndApply(provider, current with { Mode = mode, Port = port, Allow = allow }, current, output, error);
        });
        return cmd;
    }

    // ── https ────────────────────────────────────────────────────
    private static Command HttpsCommand(IServiceProvider provider, TextWriter output, TextWriter error)
    {
        var showOpt = new Option<bool>("--show", "Показать настройки HTTPS и сведения о сертификате");
        var enableOpt = new Option<bool>("--enable", "Включить HTTPS на основном порту");
        var disableOpt = new Option<bool>("--disable", "Выключить HTTPS (выбранный сертификат запоминается)");
        var thumbprintOpt = new Option<string?>("--thumbprint", "Отпечаток сертификата из хранилища «Личные» (My)");
        var storeOpt = new Option<string?>("--store", "Хранилище для --thumbprint: LocalMachine (по умолчанию). CurrentUser не поддерживается — служба работает от LocalSystem");
        var pfxOpt = new Option<string?>("--pfx", "Путь к файлу PFX");
        var pfxPasswordOpt = new Option<string?>("--pfx-password", "Пароль PFX (сохраняется зашифрованным; виден в списке процессов — вводите в доверенной консоли)");
        var selfSignedOpt = new Option<bool>("--self-signed", "Создать самоподписанный сертификат (FQDN, короткое имя, localhost)");
        var dnsOpt = new Option<string[]>("--dns", "Дополнительные DNS-имена для самоподписанного") { AllowMultipleArgumentsPerToken = true };
        var redirectOpt = new Option<int?>("--redirect-port", "HTTP-порт, который перенаправляет (308) на HTTPS; 0 — без перенаправления");
        var cmd = new Command("https", "HTTPS панели: сертификат и перенаправление с HTTP (нужны права администратора)")
        {
            showOpt, enableOpt, disableOpt, thumbprintOpt, storeOpt, pfxOpt, pfxPasswordOpt, selfSignedOpt, dnsOpt, redirectOpt,
        };

        cmd.SetHandler((InvocationContext ctx) =>
        {
            var p = ctx.ParseResult;
            bool enable = p.GetValueForOption(enableOpt), disable = p.GetValueForOption(disableOpt), selfSigned = p.GetValueForOption(selfSignedOpt);
            string? thumbprint = p.GetValueForOption(thumbprintOpt), pfx = p.GetValueForOption(pfxOpt), storeRaw = p.GetValueForOption(storeOpt);
            int? redirect = p.GetValueForOption(redirectOpt);
            bool changes = enable || disable || selfSigned || thumbprint is not null || pfx is not null || redirect is not null;

            if (p.GetValueForOption(showOpt) || !changes)
            {
                ShowHttps(provider, output);
                return;
            }

            int Fail(string message)
            {
                error.WriteLine(message);
                return 1;
            }

            if (enable && disable)
            {
                ctx.ExitCode = Fail("Укажите что-то одно: --enable или --disable.");
                return;
            }
            if ((thumbprint is not null ? 1 : 0) + (pfx is not null ? 1 : 0) + (selfSigned ? 1 : 0) > 1)
            {
                ctx.ExitCode = Fail("Укажите один источник сертификата: --thumbprint, --pfx или --self-signed.");
                return;
            }
            CertificateStoreLocation? location = null;
            if (storeRaw is not null)
            {
                if (!Enum.TryParse<CertificateStoreLocation>(storeRaw.Trim(), ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                {
                    ctx.ExitCode = Fail($"Неизвестное хранилище «{storeRaw}». Допустимо: LocalMachine, CurrentUser.");
                    return;
                }
                location = parsed;
            }

            var network = provider.GetRequiredService<INetworkSettingsService>();
            var certificates = provider.GetRequiredService<IHttpsCertificateLoader>();
            var current = network.Current;
            var https = current.Https ?? HttpsSettings.Disabled;

            if (selfSigned)
            {
                try
                {
                    var (host, domain) = MachineDnsName.Current();
                    var info = certificates.CreateSelfSigned(
                        HttpsCertificates.SelfSignedDnsNames(host, domain, p.GetValueForOption(dnsOpt) ?? []));
                    output.WriteLine($"Создан самоподписанный сертификат {info.Thumbprint}");
                    output.WriteLine($"  DNS: {string.Join(", ", info.DnsNames)}; действует до {info.NotAfter.ToLocalTime():dd.MM.yyyy}");
                    if (current.HttpsEnabled && https.Certificate?.Source == HttpsCertificateSource.SelfSigned)
                        output.WriteLine("HTTPS уже работает на самоподписанном сертификате — перезапустите службу WinAdmin, чтобы применить новый.");
                }
                catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
                {
                    ctx.ExitCode = Fail($"Не удалось создать сертификат: {ex.Message}");
                    return;
                }
            }

            var request = new UpdateHttpsRequest(
                Enabled: enable || (!disable && https.Enabled),
                Source: thumbprint is not null ? HttpsCertificateSource.Store
                    : pfx is not null ? HttpsCertificateSource.Pfx
                    : selfSigned ? HttpsCertificateSource.SelfSigned
                    : null,
                Thumbprint: thumbprint,
                StoreLocation: location,
                PfxPath: pfx is null ? null : Path.GetFullPath(pfx),
                PfxPassword: p.GetValueForOption(pfxPasswordOpt),
                HttpRedirectPort: redirect ?? https.HttpRedirectPort);
            var merged = HttpsSettingsUpdate.Merge(request, current.Https, certificates.ProtectPfxPassword);
            if ((merged.Enabled || request.Source is not null) && ServiceAccountProblem(merged.Certificate) is string problem)
            {
                ctx.ExitCode = Fail(problem);
                return;
            }

            ctx.ExitCode = CheckAndApply(provider, current with { Https = merged }, current, output, error);
        });
        return cmd;
    }

    /// <summary>
    /// Командная строка запускается от администратора, а служба — от LocalSystem: проверка в консоли пройдёт,
    /// а служба сертификат не найдёт (HTTPS не поднимется, а сторожа отката в CLI нет). Отсекаем такие случаи:
    /// хранилище CurrentUser (у SYSTEM своё) и PFX на сетевом пути (подключённые диски администратора SYSTEM не видит).
    /// </summary>
    public static string? ServiceAccountProblem(HttpsCertificateSettings? certificate, Func<string, DriveType>? driveType = null)
    {
        switch (certificate)
        {
            case { Source: HttpsCertificateSource.Store, StoreLocation: CertificateStoreLocation.CurrentUser }:
                return "Хранилище CurrentUser не подходит: служба WinAdmin работает от имени LocalSystem и не видит " +
                    "личное хранилище администратора. Импортируйте сертификат с закрытым ключом в LocalMachine\\My " +
                    "(certlm.msc → Личное) и укажите --store LocalMachine.";
            case { Source: HttpsCertificateSource.Pfx, Path: { Length: > 0 } path }:
                if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
                    return $"Файл PFX должен лежать на локальном диске сервера (например C:\\certs\\panel.pfx), а не по сетевому пути: {path}";
                string? root = Path.GetPathRoot(path);
                if (!string.IsNullOrEmpty(root) && (driveType ?? DriveTypeOf)(root) == DriveType.Network)
                    return $"Файл PFX на сетевом диске {root.TrimEnd('\\')}: служба WinAdmin (LocalSystem) не видит подключённые " +
                        "диски администратора. Скопируйте файл на локальный диск сервера (например C:\\certs\\panel.pfx).";
                return null;
            default:
                return null;
        }
    }

    private static DriveType DriveTypeOf(string root)
    {
        try { return new DriveInfo(root).DriveType; }
        catch (ArgumentException) { return DriveType.Unknown; }
    }

    private static void ShowHttps(IServiceProvider provider, TextWriter output)
    {
        var s = provider.GetRequiredService<NetworkSettingsStore>().ReadOrDefault(out _);
        var https = s.Https;
        output.WriteLine($"HTTPS:          {(s.HttpsEnabled ? "включён" : "выключен")}");
        var cert = https?.Certificate;
        if (cert is not null)
        {
            output.WriteLine($"Источник:       {cert.Source switch
            {
                HttpsCertificateSource.Store => $"хранилище {cert.StoreLocation}\\My, отпечаток {cert.Thumbprint}",
                HttpsCertificateSource.Pfx => $"PFX {cert.Path} (пароль {(string.IsNullOrEmpty(cert.Password) ? "не задан" : "задан")})",
                _ => "самоподписанный (keys\\https.pfx)",
            }}");
            var v = provider.GetRequiredService<IHttpsCertificateLoader>().Validate(cert);
            if (v.Info is { } info)
            {
                output.WriteLine($"Сертификат:     {info.Subject}, отпечаток {info.Thumbprint}");
                output.WriteLine($"DNS-имена:      {(info.DnsNames.Count == 0 ? "—" : string.Join(", ", info.DnsNames))}");
                output.WriteLine($"Срок:           действует до {info.NotAfter.ToLocalTime():dd.MM.yyyy}");
            }
            foreach (string problem in v.Errors)
                output.WriteLine($"Ошибка:         {problem}");
        }
        output.WriteLine($"Перенаправление: {(https?.HttpRedirectPort is int r ? $"HTTP {r} → HTTPS {s.Port}" : "нет")}");
        output.WriteLine($"Адрес:          {NetworkEndpoints.PanelUrl(s, null)}");
    }

    /// <summary>Проверка (включая сертификат и порты) → брандмауэр → network.json. Код выхода.</summary>
    private static int CheckAndApply(IServiceProvider provider, NetworkSettings requested, NetworkSettings current,
        TextWriter output, TextWriter error)
    {
        var network = provider.GetRequiredService<INetworkSettingsService>();
        var check = network.Check(requested);
        foreach (var message in check.Errors)
            error.WriteLine(message);
        if (check.PortBusy)
            error.WriteLine($"Порт {check.BusyPort} уже занят другой программой.");
        if (!check.Ok)
            return 1;

        try
        {
            network.Apply(check.Normalized, current);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException or IOException)
        {
            error.WriteLine($"Не удалось применить настройки: {ex.Message}");
            error.WriteLine("Запустите консоль от имени администратора.");
            return 1;
        }

        output.WriteLine($"Сохранено в {provider.GetRequiredService<NetworkSettingsStore>().FilePath}");
        output.WriteLine($"Панель: {NetworkEndpoints.PanelUrl(check.Normalized, null)}");
        output.WriteLine("Запущенная служба применит настройки автоматически (без перезапуска).");
        return 0;
    }
}
