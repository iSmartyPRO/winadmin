using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.ActiveDirectory.Folders;
using WinAdmin.Core.ActiveDirectory.Users;
using WinAdmin.Core.Import;
using WinAdmin.Core.Models;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Infrastructure.Import;

/// <summary>
/// Импорт из портала Access: сначала строится план (строки отчёта + действия), затем — только с Apply —
/// действия выполняются. Ошибка каталога AD прерывает импорт до каких-либо изменений.
/// </summary>
public sealed class AccessImporter(
    IAccessSource source, IAdStructureStore structure, IModuleRegistry modules, IRoleService roles,
    IDirectoryService directory, WinAdminDbContext db, PermissionCatalog catalog)
{
    public const string AuditWatermarkKey = "import.access.audit";
    public const string Actor = "import.access";
    private const int AuditBatch = 500;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly Dictionary<string, string[]> RoleMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["super_admin"] = [BuiltInRoles.AdministratorName],
        ["admin"] = [AdUsersModule.AdminRoleName, AdFoldersModule.AdminRoleName],
        ["hr"] = [AdUsersModule.HrRoleName],
    };

    private sealed class Step(ImportRow row, Func<CancellationToken, Task>? action)
    {
        public ImportRow Row { get; set; } = row;
        public Func<CancellationToken, Task>? Action { get; } = action;
    }

    private readonly List<Step> _steps = [];
    private readonly List<Func<CancellationToken, Task>> _settingsActions = [];
    private bool _portalUsers;

    /// <summary>«LDAP://host/OU=…,DC=…», «LDAP://OU=…» или DN → нормализованный DN; пусто — null.</summary>
    public static string? ParseLdapPath(string? ldapPath)
    {
        string value = (ldapPath ?? "").Trim();
        if (value.StartsWith("LDAP://", StringComparison.OrdinalIgnoreCase))
            value = value["LDAP://".Length..];
        int slash = value.IndexOf('/');
        if (slash >= 0 && !value[..slash].Contains('='))
            value = value[(slash + 1)..];
        value = value.Trim();
        return value.Length == 0 ? null : DnUtils.Normalize(value);
    }

    public async Task<AccessImportReport> RunAsync(AccessImportOptions options, CancellationToken ct = default)
    {
        _steps.Clear();
        _settingsActions.Clear();
        _portalUsers = false;

        var app = await source.GetAppSettingsAsync(ct);
        var current = await structure.GetAsync(ct);
        var target = current;
        string? newPassword = null;

        if (app is null)
            Add("Настройки AD (app_settings)", "app_settings", "—", ImportStatus.NotImported, "в БД Access нет строки app_settings");
        else
            (target, newPassword) = await PlanStructureAsync(app, current, options, ct);
        target = PlanHiddenOus(await source.GetExcludedOusAsync(ct), target);
        if (target != current || newPassword is not null)
        {
            var save = target;
            var pwd = newPassword;
            _settingsActions.Add(c => structure.SaveAsync(save, pwd, c));
        }

        await PlanAdUsersSettingsAsync(app, await source.GetTerminatedOusAsync(ct), options, ct);
        await PlanTemplatesAsync(ct);
        await PlanAssignmentsAsync(ct);
        await PlanLocalUsersAsync(ct);
        await PlanNotImportedAsync(app, ct);
        await PlanAuditAsync(options, ct);

        var report = new AccessImportReport { Source = source.Description, Applied = options.Apply };
        if (options.Apply)
        {
            foreach (var action in _settingsActions)
                await action(ct);
            foreach (var step in _steps.Where(s => s.Action is not null))
            {
                await step.Action!(ct);
                step.Row = step.Row with { Status = ImportStatus.Created };
            }
        }
        report.Rows.AddRange(_steps.Select(s => s.Row));
        AddHints(report, options);

        if (options.Apply)
        {
            db.AuditEntries.Add(new AuditEntryEntity
            {
                Actor = Actor, Action = "import.access", Target = source.Description, Success = true,
                Details = report.Summary() + (options.Audit ? "; с журналом" : ""),
            });
            await db.SaveChangesAsync(ct);
        }
        return report;
    }

    // ── план ─────────────────────────────────────────────────────

    private void Add(string what, string from, string to, ImportStatus status, string? note = null)
        => _steps.Add(new Step(new ImportRow(what, from, to, status, note), null));

    private void Plan(string what, string from, string to, Func<CancellationToken, Task>? action, string? note = null)
        => _steps.Add(new Step(new ImportRow(what, from, to, ImportStatus.WillCreate, note), action ?? (_ => Task.CompletedTask)));

    /// <summary>Значение «уже задано»: пусто — записать; равно — пропуск; иначе конфликт / замена с Overwrite.</summary>
    private bool Decide(string what, string from, string to, string? existing, bool same, bool overwrite)
    {
        if (string.IsNullOrWhiteSpace(existing))
        {
            Plan(what, from, to, null);
            return true;
        }
        if (same)
        {
            Add(what, from, to, ImportStatus.Exists);
            return false;
        }
        if (overwrite)
        {
            Plan(what, from, to, null, $"заменит «{existing}»");
            return true;
        }
        Add(what, from, to, ImportStatus.Conflict, $"в WinAdmin: «{existing}»; заменить — --overwrite");
        return false;
    }

    private async Task<(AdStructureSettings, string?)> PlanStructureAsync(
        AccessAppSettings app, AdStructureSettings current, AccessImportOptions options, CancellationToken ct)
    {
        var target = current;
        const string to = "Настройки → Active Directory";

        string? root = ParseLdapPath(app.LdapPath);
        if (root is null)
            Add("Корневая OU", "app_settings.ldap_path", to, ImportStatus.NotImported, "в Access не задан");
        else if (!IsOuDn(root))
            Add("Корневая OU", $"app_settings.ldap_path: {root}", to, ImportStatus.NotImported,
                "WinAdmin требует OU (OU=…,DC=…) — задайте корневую OU вручную");
        else
        {
            bool same = current.RootOu is not null && string.Equals(DnUtils.Normalize(current.RootOu), root, StringComparison.OrdinalIgnoreCase);
            if (Decide("Корневая OU", $"app_settings.ldap_path: {root}", $"{to}: корневая OU", current.RootOu, same, options.Overwrite))
                target = target with { RootOu = root };
        }

        string login = ServiceLogin(app);
        bool loginMatches = false;
        Step? replacedLogin = null;
        if (login.Length == 0)
            Add("Учётка записи", "app_settings.ad_service_username", to, ImportStatus.NotImported, "в Access не задана");
        else
        {
            bool same = SameAccount(current.WriteLogin, login);
            string? note = current.WriteMode == AdWriteMode.ProcessAccount ? "режим записи — учётка службы: логин сохранится, но не используется" : null;
            if (Decide("Учётка записи", $"app_settings.ad_service_username: {login}", $"{to}: учётка записи", current.WriteLogin, same, options.Overwrite))
            {
                target = target with { WriteLogin = login };
                loginMatches = true;
                if (note is not null) _steps[^1].Row = _steps[^1].Row with { Note = Join(_steps[^1].Row.Note, note) };
                if (!string.IsNullOrWhiteSpace(current.WriteLogin)) replacedLogin = _steps[^1];
            }
            else loginMatches = same;
        }

        string? newPassword = await PlanPasswordAsync(app, current, loginMatches, options, ct);
        // Пароль прежней учётки к новой не относится: без переноса пароля из Access — сбросить, а не оставить чужой.
        if (replacedLogin is not null && newPassword is null && current.HasWritePassword)
        {
            newPassword = "";
            replacedLogin.Row = replacedLogin.Row with
                { Note = Join(replacedLogin.Row.Note, "пароль сброшен — задайте в настройках (пароль прежней учётки к новой не относится)") };
        }
        return (target, newPassword);
    }

    private async Task<string?> PlanPasswordAsync(
        AccessAppSettings app, AdStructureSettings current, bool loginMatches, AccessImportOptions options, CancellationToken ct)
    {
        const string what = "Пароль учётки записи";
        const string from = "app_settings.ad_service_password (открытый текст)";
        const string to = "Настройки → Active Directory: пароль (шифруется)";
        string pwd = app.ServicePassword;
        if (pwd.Length == 0 || pwd.Contains("ЗАМЕНИТЕ", StringComparison.OrdinalIgnoreCase))
        {
            Add(what, from, to, ImportStatus.NotImported, "в Access не задан");
            return null;
        }
        if (!options.ImportServicePassword)
        {
            Add(what, from, to, ImportStatus.NotImported, "перенос — с флагом --import-service-password, или введите пароль в настройках");
            return null;
        }
        if (!loginMatches)
        {
            Add(what, from, to, ImportStatus.NotImported, "учётка записи в WinAdmin другая — пароль к ней не относится");
            return null;
        }
        if (current.HasWritePassword && SameAccount(current.WriteLogin, ServiceLogin(app)))
        {
            string? existing;
            try { existing = (await structure.GetWriteCredentialAsync(ct)).Password; }
            catch (SecretUnavailableException) { existing = null; }
            if (existing == pwd)
            {
                Add(what, from, to, ImportStatus.Exists);
                return null;
            }
            if (!options.Overwrite)
            {
                Add(what, from, to, ImportStatus.Conflict, "в WinAdmin задан другой пароль; заменить — --overwrite");
                return null;
            }
        }
        Plan(what, from, to, null);
        return pwd;
    }

    private AdStructureSettings PlanHiddenOus(IReadOnlyList<string> excluded, AdStructureSettings target)
    {
        var hidden = target.HiddenOus.ToList();
        foreach (var raw in excluded)
        {
            string ou = raw.Trim();
            if (ou.Length == 0) continue;
            string what = $"Скрытая OU: {ou}";
            if (hidden.Contains(ou, StringComparer.OrdinalIgnoreCase))
                Add(what, "excluded_ous", "Настройки → Active Directory: скрытые OU", ImportStatus.Exists);
            else
            {
                hidden.Add(ou);
                Plan(what, "excluded_ous", "Настройки → Active Directory: скрытые OU", null);
            }
        }
        return hidden.Count == target.HiddenOus.Count ? target : target with { HiddenOus = hidden };
    }

    private async Task PlanAdUsersSettingsAsync(
        AccessAppSettings? app, IReadOnlyList<AccessTerminatedOu> terminated, AccessImportOptions options, CancellationToken ct)
    {
        var settings = await modules.GetSettingsAsync<AdUsersSettings>(AdUsersModule.ModuleId, ct);
        var changes = new JsonObject();
        const string to = "Модуль «Пользователи AD»";

        string fired = app?.FiredUsersGroup.Trim() ?? "";
        if (app is not null && fired.Length == 0)
            Add("Группа уволенных", "app_settings.fired_users_group", to, ImportStatus.NotImported, "в Access не задана");
        else if (fired.Length > 0
                 && Decide("Группа уволенных", $"app_settings.fired_users_group: {fired}", $"{to}: группа уволенных",
                     settings.FiredGroup, string.Equals(settings.FiredGroup.Trim(), fired, StringComparison.OrdinalIgnoreCase), options.Overwrite))
            changes["firedGroup"] = fired;

        foreach (var ou in terminated)
        {
            string what = $"OU уволенных: {ou.OuName}";
            if (!ou.IsDeactivationDefault)
            {
                Add(what, "terminated_user_ous", to, ImportStatus.NotImported,
                    "в WinAdmin одна OU уволенных — переносится только OU «по умолчанию для деактивации»");
                continue;
            }
            string? dn = ParseLdapPath(ou.OuDn);
            if (dn is null)
            {
                Add(what, "terminated_user_ous", to, ImportStatus.NotImported, "в Access у OU нет DN — задайте OU уволенных вручную");
                continue;
            }
            bool same = settings.TerminatedOuDn.Length > 0
                        && string.Equals(DnUtils.Normalize(settings.TerminatedOuDn), dn, StringComparison.OrdinalIgnoreCase);
            if (Decide(what, $"terminated_user_ous: {dn}", $"{to}: OU уволенных", settings.TerminatedOuDn, same, options.Overwrite))
                changes["terminatedOuDn"] = dn;
        }

        if (changes.Count > 0)
            _settingsActions.Add(c => modules.SaveSettingsAsync(AdUsersModule.ModuleId, changes, Actor, c));
    }

    private async Task PlanTemplatesAsync(CancellationToken ct)
    {
        var existing = (await roles.ListAsync(ct)).Select(r => r.Name).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        foreach (var template in AdRoleTemplates.All)
        {
            string what = $"Роль-шаблон «{template.Name}»";
            if (existing.Contains(template.Name))
                Add(what, "—", "Роли", ImportStatus.Exists);
            else
                Plan(what, "—", "Роли", c => AdRoleTemplates.EnsureAsync(roles, catalog, [template], c));
        }
    }

    private async Task PlanAssignmentsAsync(CancellationToken ct)
    {
        var existing = (await roles.ListAssignmentsAsync(ct: ct))
            .Select(a => Key(a.RoleName, a.PrincipalType, a.PrincipalId)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var p in await source.GetRolePrincipalsAsync(ct))
        {
            string what = $"Роль Access «{p.RoleCode}»: {p.PrincipalType} {p.DisplayName}";
            string from = $"role_principals: {p.DistinguishedName}";
            if (!RoleMap.TryGetValue(p.RoleCode, out var targets))
            {
                Add(what, from, "—", ImportStatus.NotImported, "нет соответствующей роли в WinAdmin");
                continue;
            }
            var found = await directory.FindByDnAsync(p.DistinguishedName, ct);
            if (found is null)
            {
                Add(what, from, string.Join(", ", targets.Select(t => $"«{t}»")), ImportStatus.NotFoundInAd);
                continue;
            }
            PlanAssign(what, from, targets, found, existing);
        }

        foreach (var u in await source.GetPortalUsersAsync(ct))
        {
            string what = $"Пользователь портала {u.SamAccountName} («{u.RoleCode}»)";
            const string from = "portal_users";
            if (!u.IsActive)
            {
                Add(what, from, "—", ImportStatus.NotImported, "учётка отключена в Access");
                continue;
            }
            if (!RoleMap.TryGetValue(u.RoleCode, out var targets))
            {
                Add(what, from, "—", ImportStatus.NotImported, "нет соответствующей роли в WinAdmin");
                continue;
            }
            var found = await directory.FindUserAsync(u.SamAccountName, ct);
            if (found is null)
            {
                Add(what, from, string.Join(", ", targets.Select(t => $"«{t}»")), ImportStatus.NotFoundInAd);
                continue;
            }
            _portalUsers = true;
            PlanAssign(what, from, targets, found, existing);
        }
    }

    private void PlanAssign(string what, string from, string[] targets, DirectoryObject found, HashSet<string> existing)
    {
        var type = found.Kind == DirectoryObjectKind.Group ? PrincipalType.AdGroup : PrincipalType.AdUser;
        string display = found.DisplayName ?? found.SamAccountName;
        foreach (var roleName in targets)
        {
            string to = $"«{roleName}» → {(type == PrincipalType.AdGroup ? "группа" : "пользователь")} {found.SamAccountName} ({found.Sid})";
            // Добавляем в множество сразу: тот же субъект из role_principals и portal_users назначается один раз.
            if (!existing.Add(Key(roleName, type, found.Sid)))
            {
                Add(what, from, to, ImportStatus.Exists);
                continue;
            }
            Plan(what, from, to, async c =>
            {
                var role = (await roles.ListAsync(c)).Single(r => string.Equals(r.Name, roleName, StringComparison.CurrentCultureIgnoreCase));
                await roles.AssignAsync(new CreateAssignmentRequest(role.Id, type, found.Sid, display),
                    AdRoleTemplates.SystemContext(catalog, Actor), c);
            });
        }
    }

    private static string Key(string role, PrincipalType type, string id) => $"{role}|{type}|{id}";

    private async Task PlanLocalUsersAsync(CancellationToken ct)
    {
        foreach (var u in await source.GetLocalUsersAsync(ct))
            Add($"Локальный пользователь {u.Username} («{u.RoleCode}»{(u.IsActive ? "" : ", отключён")})", "local_users", "—",
                ImportStatus.NotImported,
                $"другой формат хеша пароля — создайте заново: WinAdmin.exe user add --login {u.Username} --password … --role …");
    }

    private async Task PlanNotImportedAsync(AccessAppSettings? app, CancellationToken ct)
    {
        var extras = await source.GetExtrasAsync(ct);
        if (extras.ProjectMetadata > 0)
            Add("Метаданные проектов (адрес, город, координаты, HR-почта)", $"project_metadata: {extras.ProjectMetadata}", "—",
                ImportStatus.NotImported, "в WinAdmin нет раздела проектов");
        if (extras.MailGroups > 0)
            Add("Рассылки проектов", $"project_metadata.mail_group_*: {extras.MailGroups}", "—", ImportStatus.NotImported,
                "рассылки в WinAdmin пока не поддерживаются");
        if (app is null) return;
        if (app.ExchangeUrl.Trim().Length > 0)
            Add("Exchange", $"app_settings.exchange_url: {app.ExchangeUrl.Trim()}", "—", ImportStatus.NotImported, "в WinAdmin нет интеграции с Exchange");
        if (app.HasApiSettings)
            Add("API-настройки (ключ Яндекс.Карт)", "app_settings.api_settings", "—", ImportStatus.NotImported, "не используются в WinAdmin");
        if (app.Domain.Trim().Length > 0)
            Add("Домен", $"app_settings.domain: {app.Domain.Trim()}", "Настройки → Подключение к домену", ImportStatus.NotImported,
                "задаётся при подключении к домену");
    }

    private async Task PlanAuditAsync(AccessImportOptions options, CancellationToken ct)
    {
        long lastId = await AuditWatermarkAsync(ct);
        long count = await source.CountAuditAsync(lastId, ct);
        string from = lastId > 0 ? $"audit_log: {count} новых записей (после id {lastId})" : $"audit_log: {count} записей";
        const string to = "Журнал аудита WinAdmin (access.*)";
        if (!options.Audit)
        {
            if (count > 0)
                Add("Журнал аудита", from, to, ImportStatus.NotImported, "перенос — с флагом --audit");
            return;
        }
        if (count == 0)
        {
            Add("Журнал аудита", from, to, ImportStatus.Exists);
            return;
        }
        Plan("Журнал аудита", from, to, c => ImportAuditAsync(lastId, c), "пароли из details удаляются");
    }

    private void AddHints(AccessImportReport report, AccessImportOptions options)
    {
        var disabled = new[] { AdUsersModule.ModuleId, AdFoldersModule.ModuleId }
            .Select(id => modules.Modules.First(m => m.Id == id))
            .Where(m => !modules.GetState(m.Id).Enabled).Select(m => $"«{m.Title}»").ToList();
        if (disabled.Count > 0)
            report.Hints.Add($"Включите модули {string.Join(" и ", disabled)} (Настройки → Модули), затем проверьте окружение.");
        if (_portalUsers)
            report.Hints.Add("В Access роль из portal_users заменяла роль из групп, в WinAdmin роли суммируются — " +
                             "проверьте в «Роли», что у этих пользователей нет лишних ролей через группы.");
        if (!options.Apply)
            report.Hints.Add("Это только отчёт. Для импорта повторите команду с --apply.");
        else if (_settingsActions.Count > 0)
            report.Hints.Add("Перезапустите службу WinAdmin, чтобы она перечитала настройки.");
    }

    // ── журнал ───────────────────────────────────────────────────

    private async Task<long> AuditWatermarkAsync(CancellationToken ct)
    {
        var row = await db.PlatformSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == AuditWatermarkKey, ct);
        return row is null ? 0 : JsonNode.Parse(row.Json)?["lastId"]?.GetValue<long>() ?? 0;
    }

    private async Task ImportAuditAsync(long lastId, CancellationToken ct)
    {
        while (true)
        {
            var batch = await source.ReadAuditAsync(lastId, AuditBatch, ct);
            if (batch.Count == 0) return;
            foreach (var e in batch)
                db.AuditEntries.Add(new AuditEntryEntity
                {
                    Timestamp = e.CreatedAt, Actor = e.ActorSam, Action = "access." + e.Action,
                    Target = e.TargetId, Success = true, Details = AuditDetails(e),
                });
            lastId = batch[^1].Id;
            var mark = await db.PlatformSettings.FirstOrDefaultAsync(s => s.Key == AuditWatermarkKey, ct);
            if (mark is null) db.PlatformSettings.Add(mark = new PlatformSettingEntity { Key = AuditWatermarkKey });
            mark.Json = new JsonObject { ["lastId"] = lastId }.ToJsonString();
            mark.UpdatedAt = DateTimeOffset.UtcNow;
            // Записи порции и отметка — одним SaveChanges (одна транзакция): прерванный импорт продолжится без дублей.
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>{ accessId, targetType, details } — ключи с «password» в details удаляются на любой глубине.</summary>
    public static string AuditDetails(AccessAuditEntry e)
    {
        var result = new JsonObject { ["accessId"] = e.Id };
        if (e.TargetType is not null) result["targetType"] = e.TargetType;
        if (!string.IsNullOrWhiteSpace(e.DetailsJson))
        {
            JsonNode? details;
            try { details = JsonNode.Parse(e.DetailsJson); }
            catch (JsonException) { details = null; }
            if (details is not null)
            {
                StripPasswords(details);
                result["details"] = details;
            }
        }
        return result.ToJsonString(Json);
    }

    private static void StripPasswords(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(kv => kv.Key).Where(k => k.Contains("password", StringComparison.OrdinalIgnoreCase)).ToList())
                obj.Remove(key);
            foreach (var child in obj.Select(kv => kv.Value).OfType<JsonNode>())
                StripPasswords(child);
        }
        else if (node is JsonArray arr)
            foreach (var child in arr.OfType<JsonNode>())
                StripPasswords(child);
    }

    // ── помощники ────────────────────────────────────────────────

    /// <summary>Как SecureConfig.ApplyAdSettings в Access: «ДОМЕН\имя» (домен учётки или домен портала).</summary>
    private static string ServiceLogin(AccessAppSettings app)
    {
        string sam = app.ServiceUsername.Trim();
        if (sam.Length == 0) return "";
        int slash = sam.IndexOf('\\');
        if (slash >= 0) sam = sam[(slash + 1)..];
        int at = sam.IndexOf('@');
        if (at >= 0) sam = sam[..at];
        string domain = string.IsNullOrWhiteSpace(app.ServiceDomain) ? app.Domain.Trim() : app.ServiceDomain.Trim();
        return domain.Length > 0 ? $"{domain}\\{sam}" : sam;
    }

    private static bool SameAccount(string? a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        try
        {
            return string.Equals(DirectoryLogin.Parse(a).Sam, DirectoryLogin.Parse(b).Sam, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Годится ли DN как корневая OU WinAdmin (те же правила, что AdStructureSettings.Normalize).</summary>
    private static bool IsOuDn(string dn)
    {
        try
        {
            (AdStructureSettings.Default with { RootOu = dn }).Normalize();
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string Join(string? a, string b) => string.IsNullOrEmpty(a) ? b : $"{a}; {b}";
}
