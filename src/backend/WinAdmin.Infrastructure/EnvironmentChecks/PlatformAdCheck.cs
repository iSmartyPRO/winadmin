using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.EnvironmentChecks;

namespace WinAdmin.Infrastructure.EnvironmentChecks;

/// <summary>Готовность общего слоя AD: домен, корневая OU, учётка записи, шифрование канала.</summary>
public sealed class PlatformAdCheck(
    IDirectorySettingsStore directory, IDirectoryService directoryService, IAdStructureStore structure,
    IAdReader reader, TimeProvider? time = null) : IEnvironmentCheck
{
    private const string SettingsFix = "Настройки → Active Directory";
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public string ModuleId => EnvironmentService.Platform;

    public async Task<IReadOnlyList<CheckResult>> RunAsync(CheckDepth depth, CancellationToken ct)
    {
        var results = new List<CheckResult>();

        // 1. Подключение к домену.
        var dir = await directory.GetAsync(ct);
        CheckResult domain;
        if (!dir.Enabled)
            domain = CheckResult.Fail("ad.directory", "Подключение к домену", "Подключение к домену выключено",
                "Настройки → Подключение к домену: включите и укажите домен");
        else
        {
            var steps = await directoryService.TestConnectionAsync(ct);
            var failed = steps.FirstOrDefault(s => !s.Ok);
            domain = failed is null
                ? CheckResult.Ok("ad.directory", "Подключение к домену", $"Домен {dir.Domain}: контроллер отвечает")
                : CheckResult.Fail("ad.directory", "Подключение к домену", $"{failed.Name}: {failed.Message}",
                    "Настройки → Подключение к домену → «Проверить»");
        }
        results.Add(domain);
        if (domain.Status == CheckStatus.Failed)
        {
            results.Add(CheckResult.Skip("ad.root", "Корневая OU", "Нет подключения к домену"));
            results.Add(CheckResult.Skip("ad.writer", "Учётка записи", "Нет подключения к домену"));
            results.Add(CheckResult.Skip("ad.channel", "Шифрование канала", "Нет подключения к домену"));
            return results;
        }

        // 2. Корневая OU и проекты.
        var st = await structure.GetAsync(ct);
        results.Add(await RootAsync(st, ct));

        // 3. Учётка записи.
        CheckResult writer;
        WriterStatus? status = null;
        if (st.WriteMode == AdWriteMode.ServiceAccount && (st.WriteLogin is null || !st.HasWritePassword))
            writer = CheckResult.Fail("ad.writer", "Учётка записи", "Служебная учётка не задана: нужен логин и пароль",
                SettingsFix + ": укажите служебную учётку или выберите «Учётка службы»");
        else
        {
            status = await reader.GetWriterStatusAsync(ct);
            writer = Writer(status);
        }
        results.Add(writer);

        // 4. Шифрование канала записи.
        results.Add(status is { Bound: true }
            ? status.Encrypted
                ? CheckResult.Ok("ad.channel", "Шифрование канала", dir.UseLdaps ? "LDAPS (636)" : "LDAP 389 с подписью и шифрованием")
                : CheckResult.Fail("ad.channel", "Шифрование канала", "Канал не зашифрован — AD не позволит менять пароли",
                    "Включите LDAPS (Настройки → Подключение к домену) или проверьте Kerberos между сервером и DC")
            : CheckResult.Skip("ad.channel", "Шифрование канала", "Учётка записи не вошла"));
        return results;
    }

    private async Task<CheckResult> RootAsync(AdStructureSettings st, CancellationToken ct)
    {
        if (st.RootOu is null)
            return CheckResult.Fail("ad.root", "Корневая OU", "Корневая OU не задана", SettingsFix + ": укажите DN корневой OU (например OU=Accounts,DC=…)");
        if (!await reader.ExistsAsync(st.RootOu, ct))
            return CheckResult.Fail("ad.root", "Корневая OU", $"OU не найдена: {st.RootOu}", SettingsFix + ": проверьте DN");

        var projects = await reader.ListProjectsAsync(false, ct);
        var missingHidden = new List<string>();
        foreach (var hidden in st.HiddenOus)
            if (!await reader.ExistsAsync($"OU={hidden},{st.RootOu}", ct)) missingHidden.Add(hidden);

        string message = $"{st.RootOu}: проектов {projects.Count}";
        return missingHidden.Count > 0
            ? CheckResult.Warn("ad.root", "Корневая OU", message + $"; скрытые OU не найдены: {string.Join(", ", missingHidden)}",
                SettingsFix + ": уберите лишние скрытые OU")
            : CheckResult.Ok("ad.root", "Корневая OU", message);
    }

    private CheckResult Writer(WriterStatus s)
    {
        if (!s.Bound)
            return CheckResult.Fail("ad.writer", "Учётка записи", s.Error ?? "Вход не выполнен", SettingsFix + ": проверьте логин и пароль");
        if (s.Enabled == false)
            return CheckResult.Fail("ad.writer", "Учётка записи", $"{s.Account}: учётка отключена в AD", "Включите учётку в AD");
        if (s.Locked == true)
            return CheckResult.Fail("ad.writer", "Учётка записи", $"{s.Account}: учётка заблокирована", "Разблокируйте учётку в AD");
        if (s.PasswordExpires is { } expires && expires - _time.GetUtcNow() < TimeSpan.FromDays(14))
            return CheckResult.Warn("ad.writer", "Учётка записи", $"{s.Account}: пароль истекает {expires:dd.MM.yyyy}",
                "Смените пароль в AD и обновите его в " + SettingsFix);
        return CheckResult.Ok("ad.writer", "Учётка записи", $"Вход выполнен: {s.Account}");
    }
}
