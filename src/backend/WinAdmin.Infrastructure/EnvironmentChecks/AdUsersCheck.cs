using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.ActiveDirectory.Users;
using WinAdmin.Core.EnvironmentChecks;
using WinAdmin.Infrastructure.ActiveDirectory;

namespace WinAdmin.Infrastructure.EnvironmentChecks;

/// <summary>Готовность модуля «Пользователи AD»: OU пользователей, права учётки записи, группа и OU уволенных, сброс пароля.</summary>
public sealed class AdUsersCheck(IAdReader reader, IAdUserDirectory users, IAdStructureStore structure, IModuleRegistry modules) : IEnvironmentCheck
{
    private const int MaxProjects = 50;
    private static readonly string[] Codes =
        ["users.usersOu", "ad.rights.users", "users.fired", "users.domainUsers", "users.terminatedOu", "users.resetPassword"];

    public string ModuleId => AdUsersModule.ModuleId;

    public async Task<IReadOnlyList<CheckResult>> RunAsync(CheckDepth depth, CancellationToken ct)
    {
        var st = await structure.GetAsync(ct);
        if (st.RootOu is null)
            return Codes.Select(c => CheckResult.Skip(c, Title(c), "Корневая OU не задана (см. «Платформа»)")).ToList();

        var settings = await modules.GetSettingsAsync<AdUsersSettings>(AdUsersModule.ModuleId, ct);
        var projects = (await reader.ListProjectsAsync(false, ct)).Take(MaxProjects).ToList();
        var writer = await reader.GetWriterStatusAsync(ct);
        string account = writer.Account;
        var results = new List<CheckResult>();

        // OU пользователей в проектах + образцы пользователей.
        var missingOu = new List<string>();
        var samples = new List<(AdProject Project, string Dn)>();
        foreach (var p in projects)
        {
            string ou = $"OU={st.UsersOuName},{p.Dn}";
            if (!await reader.ExistsAsync(ou, ct)) { missingOu.Add(p.Name); continue; }
            if (await users.FindSampleUserAsync(ou, ct) is { } sample) samples.Add((p, sample));
        }
        results.Add(missingOu.Count == 0
            ? CheckResult.Ok("users.usersOu", Title("users.usersOu"), $"OU «{st.UsersOuName}» есть во всех проектах ({projects.Count})")
            : CheckResult.Warn("users.usersOu", Title("users.usersOu"), $"Нет OU «{st.UsersOuName}» в проектах: {string.Join(", ", missingOu)}",
                "Создайте OU или измените «OU пользователей» в Настройки → Active Directory"));

        if (!writer.Bound)
        {
            string reason = "Учётка записи не вошла (см. «Платформа»)";
            results.Add(CheckResult.Skip("ad.rights.users", Title("ad.rights.users"), reason));
            results.Add(await FiredAsync(settings, null, ct));
            results.Add(CheckResult.Skip("users.domainUsers", Title("users.domainUsers"), reason));
            results.Add(await TerminatedOuAsync(settings, null, ct));
            results.Add(CheckResult.Skip("users.resetPassword", Title("users.resetPassword"), reason));
            return results;
        }

        // Права на атрибуты пользователей.
        var needed = settings.EditableAttributes.Append("thumbnailPhoto").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var lacking = new List<string>();
        foreach (var (project, dn) in samples)
        {
            var rights = await reader.ReadEffectiveAsync(dn, ct);
            var missing = needed.Where(a => !rights.Attributes.Contains(a)).ToList();
            if (missing.Count > 0) lacking.Add($"{project.Name}: {string.Join(", ", missing)}");
        }
        results.Add(samples.Count == 0
            ? CheckResult.Warn("ad.rights.users", Title("ad.rights.users"), "В проектах нет пользователей для проверки")
            : lacking.Count == 0
                ? CheckResult.Ok("ad.rights.users", Title("ad.rights.users"), $"Запись атрибутов разрешена ({samples.Count} проектов)")
                : CheckResult.Fail("ad.rights.users", Title("ad.rights.users"), "Нет права записи: " + string.Join("; ", lacking),
                    $"Делегируйте {account} «Write Property» на эти атрибуты для объектов user в OU={st.UsersOuName} проектов"));

        results.Add(await FiredAsync(settings, account, ct));

        var domainUsers = await users.GetDomainUsersGroupAsync(ct);
        results.Add((await reader.ReadEffectiveAsync(domainUsers.Dn, ct)).Attributes.Contains("member")
            ? CheckResult.Ok("users.domainUsers", Title("users.domainUsers"), $"«{domainUsers.Name}»: запись участников разрешена")
            : CheckResult.Fail("users.domainUsers", Title("users.domainUsers"), $"«{domainUsers.Name}»: нет права изменять участников",
                $"Делегируйте {account} «Write members» на группу «{domainUsers.Name}»"));

        results.Add(await TerminatedOuAsync(settings, account, ct));

        if (samples.Count == 0)
            results.Add(CheckResult.Skip("users.resetPassword", Title("users.resetPassword"), "Нет пользователей для проверки"));
        else
        {
            var sids = await users.GetWriterSidsAsync(ct);
            var sd = await users.ReadSecurityDescriptorAsync(samples[0].Dn, ct);
            bool can = sd is not null && AclInspector.HasExtendedRight(sd, sids, AclInspector.ResetPassword);
            results.Add(can
                ? CheckResult.Ok("users.resetPassword", Title("users.resetPassword"), "Сброс пароля разрешён")
                : CheckResult.Fail("users.resetPassword", Title("users.resetPassword"), $"Нет права «Reset Password» (проверено на {samples[0].Project.Name})",
                    $"Делегируйте {account} «Reset Password» на объекты user в OU={st.UsersOuName} проектов"));
        }
        return results;
    }

    private async Task<CheckResult> FiredAsync(AdUsersSettings settings, string? account, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.FiredGroup))
            return CheckResult.Warn("users.fired", Title("users.fired"), "Группа уволенных не задана — увольнение недоступно",
                "Модули → «Пользователи AD» → настройки: «Группа уволенных»");
        var group = await users.FindGroupAsync(settings.FiredGroup, ct);
        if (group is null)
            return CheckResult.Fail("users.fired", Title("users.fired"), $"Группа «{settings.FiredGroup}» не найдена", "Проверьте имя или DN группы");
        if (account is null) return CheckResult.Ok("users.fired", Title("users.fired"), $"Группа найдена: {group.Dn}");
        return (await reader.ReadEffectiveAsync(group.Dn, ct)).Attributes.Contains("member")
            ? CheckResult.Ok("users.fired", Title("users.fired"), $"«{group.Name}»: запись участников разрешена")
            : CheckResult.Fail("users.fired", Title("users.fired"), $"«{group.Name}»: нет права изменять участников",
                $"Делегируйте {account} «Write members» на группу «{group.Name}»");
    }

    private async Task<CheckResult> TerminatedOuAsync(AdUsersSettings settings, string? account, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.TerminatedOuDn))
            return CheckResult.Warn("users.terminatedOu", Title("users.terminatedOu"), "OU уволенных не задана — увольнение недоступно",
                "Модули → «Пользователи AD» → настройки: «OU уволенных»");
        if (!await reader.ExistsAsync(settings.TerminatedOuDn, ct))
            return CheckResult.Fail("users.terminatedOu", Title("users.terminatedOu"), $"OU не найдена: {settings.TerminatedOuDn}", "Проверьте DN");
        if (account is null) return CheckResult.Ok("users.terminatedOu", Title("users.terminatedOu"), "OU найдена");
        return (await reader.ReadEffectiveAsync(settings.TerminatedOuDn, ct)).ChildClasses.Contains("user")
            ? CheckResult.Ok("users.terminatedOu", Title("users.terminatedOu"), "Перенос в OU уволенных разрешён")
            : CheckResult.Fail("users.terminatedOu", Title("users.terminatedOu"), "Нет права переносить пользователей в OU уволенных",
                $"Делегируйте {account} «Create/Delete User objects» на OU уволенных и OU пользователей проектов");
    }

    private static string Title(string code) => code switch
    {
        "users.usersOu" => "OU пользователей в проектах",
        "ad.rights.users" => "Права на атрибуты пользователей",
        "users.fired" => "Группа уволенных",
        "users.domainUsers" => "Пользователи домена",
        "users.terminatedOu" => "OU уволенных",
        "users.resetPassword" => "Сброс пароля",
        _ => code,
    };
}
