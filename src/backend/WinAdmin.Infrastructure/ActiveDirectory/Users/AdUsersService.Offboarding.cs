using Microsoft.Extensions.Logging;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.ActiveDirectory.Users;
using WinAdmin.Core.Operations;
using WinAdmin.Core.Security;

namespace WinAdmin.Infrastructure.ActiveDirectory.Users;

public sealed partial class AdUsersService
{
    /// <summary>Увольнение (порт AdUserDeactivation): Fired Users + основная → снять группы → пароль → отключить → OU уволенных.</summary>
    public async Task<IReadOnlyList<ScenarioStep>> DeactivateAsync(IAccessContext actor, string sam, CancellationToken ct = default)
    {
        var (st, settings) = await ConfigAsync(ct);
        RequireOffboardSettings(settings);
        var located = await LocateAsync(actor, sam, PermissionIds.AdUsersOffboard, st, settings, ct);
        var fired = await directory.FindGroupAsync(settings.FiredGroup, ct)
                    ?? throw new InvalidOperationException($"Группа уволенных «{settings.FiredGroup}» не найдена в AD.");
        var user = located.User;
        var run = new ScenarioRunner(ex => logger.LogError(ex, "Увольнение {Sam}", sam));

        await run.RunAsync($"Добавление в группу «{fired.Name}»", async () =>
            await writer.AddMemberAsync(fired.Dn, user.Dn, ct) ? StepResult.Done() : StepResult.Skip("уже в группе"));
        await run.RunAsync($"Основная группа «{fired.Name}»", async () =>
        {
            if (Rid(fired.Sid) == user.PrimaryGroupId.ToString()) return StepResult.Skip("уже основная");
            await writer.SetPrimaryGroupAsync(user.Dn, fired.Sid!, ct);
            return StepResult.Done();
        });

        if (!run.Failed)
        {
            var current = await directory.FindUserAsync(sam, [], ct) ?? user;
            foreach (var group in (await directory.GetGroupsAsync(current, ct)).Where(g => !g.IsPrimary && !SameDn(g.Dn, fired.Dn)))
                await run.RunAsync($"Удаление из группы «{group.Name}»", async () =>
                    await writer.RemoveMemberAsync(group.Dn, user.Dn, ct) ? StepResult.Done() : StepResult.Skip("не состоял"));
        }

        await run.RunAsync("Сброс пароля", async () =>
        {
            await writer.ResetPasswordAsync(user.Dn, PasswordGenerator.Generate(), false, ct);
            return StepResult.Done("случайный, не показывается");
        });
        await run.RunAsync("Отключение учётной записи", async () =>
        {
            if (!user.Enabled) return StepResult.Skip("уже отключена");
            await writer.SetEnabledAsync(user.Dn, false, ct);
            return StepResult.Done();
        });
        await run.RunAsync("Перенос в OU уволенных", async () =>
        {
            if (located.Terminated) return StepResult.Skip("уже в OU уволенных");
            await writer.MoveAsync(user.Dn, settings.TerminatedOuDn, ct);
            return StepResult.Done();
        });

        await AuditAsync(actor, "user.account.deactivate", user.Dn, !run.Failed, Summary(run.Steps), ct);
        return run.Steps;
    }

    /// <summary>Восстановление (порт AdUserActivation): «Пользователи домена» + основная → снять Fired → перенос → пароль → включить.</summary>
    public async Task<ActivationResult> ActivateAsync(IAccessContext actor, string sam, string projectDn, CancellationToken ct = default)
    {
        var (st, settings) = await ConfigAsync(ct);
        var located = await LocateAsync(actor, sam, PermissionIds.AdUsersOffboard, st, settings, ct);
        string project = AdGuard.EnsureManaged(projectDn, st);
        AdGuard.EnsureInScope(actor, PermissionIds.AdUsersOffboard, project);
        string targetOu = $"OU={st.UsersOuName},{project}";
        if (!await reader.ExistsAsync(targetOu, ct))
            throw new InvalidOperationException($"В проекте нет OU «{st.UsersOuName}».");

        var domainUsers = await directory.GetDomainUsersGroupAsync(ct);
        var fired = string.IsNullOrWhiteSpace(settings.FiredGroup) ? null : await directory.FindGroupAsync(settings.FiredGroup, ct);
        var user = located.User;
        string dn = user.Dn;
        string? password = null;
        var run = new ScenarioRunner(ex => logger.LogError(ex, "Восстановление {Sam}", sam));

        await run.RunAsync($"Добавление в «{domainUsers.Name}»", async () =>
            user.PrimaryGroupId == 513 || await writer.AddMemberAsync(domainUsers.Dn, dn, ct) ? StepResult.Done() : StepResult.Skip("уже в группе"));
        await run.RunAsync($"Основная группа «{domainUsers.Name}»", async () =>
        {
            if (user.PrimaryGroupId == 513) return StepResult.Skip("уже основная");
            await writer.SetPrimaryGroupAsync(dn, domainUsers.Sid!, ct);
            return StepResult.Done();
        });
        if (fired is not null)
            await run.RunAsync($"Удаление из группы «{fired.Name}»", async () =>
                await writer.RemoveMemberAsync(fired.Dn, dn, ct) ? StepResult.Done() : StepResult.Skip("не состоял"));
        await run.RunAsync($"Перенос в «{DnUtils.RelativeOuPath(project, st.RootOu!)[0]}»", async () =>
        {
            if (SameDn(AdWriteRequests.Parent(dn), targetOu)) return StepResult.Skip("уже там");
            dn = await writer.MoveAsync(dn, targetOu, ct);
            return StepResult.Done();
        });
        await run.RunAsync("Новый пароль", async () =>
        {
            string value = PasswordGenerator.Generate();
            await writer.ResetPasswordAsync(dn, value, true, ct);
            password = value;
            return StepResult.Done("показан оператору, смена при входе");
        });
        await run.RunAsync("Включение учётной записи", async () =>
        {
            await writer.SetEnabledAsync(dn, true, ct);
            return StepResult.Done();
        });

        await AuditAsync(actor, "user.account.activate", user.Dn, !run.Failed, Summary(run.Steps), ct);
        return new ActivationResult(run.Steps, run.Failed ? null : password);
    }

    private static void RequireOffboardSettings(AdUsersSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.FiredGroup))
            throw new InvalidOperationException("Не задана группа уволенных (настройки модуля «Пользователи AD»).");
        if (string.IsNullOrWhiteSpace(settings.TerminatedOuDn))
            throw new InvalidOperationException("Не задана OU уволенных (настройки модуля «Пользователи AD»).");
    }

    private static string? Rid(string? sid) => sid?[(sid.LastIndexOf('-') + 1)..];

    private static bool SameDn(string a, string b) => DnUtils.IsUnderOrSame(a, b) && DnUtils.IsUnderOrSame(b, a);

    private static string Summary(IReadOnlyList<ScenarioStep> steps)
        => string.Join("; ", steps.Select(s => $"{s.Name}: {s.Status}" + (s.Status == StepStatus.Failed ? $" ({s.Message})" : "")));
}
