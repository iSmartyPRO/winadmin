using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.ActiveDirectory.Folders;
using WinAdmin.Core.ActiveDirectory.Users;
using WinAdmin.Core.Import;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure.Access;
using WinAdmin.Infrastructure.ActiveDirectory;
using WinAdmin.Infrastructure.Import;
using WinAdmin.Infrastructure.Modules;
using WinAdmin.Infrastructure.Secrets;
using WinAdmin.Infrastructure.Security;
using WinAdmin.Infrastructure.Storage;
using WinAdmin.Tests.Fakes;

namespace WinAdmin.Tests;

public sealed class AccessImporterTests : IDisposable
{
    private const string Secret = "Acc3ss-Svc-Pa55!";
    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private readonly ServiceProvider _sp;
    private readonly FakeAccessSource _access = new();
    private readonly FakeDirectory _directory = new();
    private readonly DirectoryObject _admins, _hr, _ivanov, _petrov;

    public AccessImporterTests()
    {
        _connection.Open();
        var catalog = new PermissionCatalog(BuiltInModules.All);
        _sp = new ServiceCollection()
            .AddDbContext<WinAdminDbContext, SqliteWinAdminDbContext>(o => o.UseSqlite(_connection))
            .AddSingleton(catalog)
            .AddSingleton(Mock.Of<IAccessService>())
            .AddScoped<IAuditService, AuditService>()
            .AddScoped<IRoleService, RoleService>()
            .AddSingleton<IDirectoryService>(_directory)
            .AddSingleton(Mock.Of<IMachineInfo>(m => m.IsDomainJoined == true))
            .AddSingleton<ISecretProtector>(new AesGcmSecretProtector(RandomNumberGenerator.GetBytes(32)))
            .AddSingleton<IModuleRegistry, ModuleRegistry>()
            .AddSingleton<IAdStructureStore, AdStructureStore>()
            .AddSingleton<IAccessSource>(_access)
            .AddScoped<AccessImporter>()
            .BuildServiceProvider();
        using (var scope = _sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WinAdminDbContext>();
            db.Database.Migrate();
            PlatformBootstrapper.RunAsync(db, catalog).GetAwaiter().GetResult();
        }

        _admins = _directory.AddGroup("PortalAdmins");
        _hr = _directory.AddGroup("HR");
        _ivanov = _directory.AddUser("ivanov", "x");
        _petrov = _directory.AddUser("petrov", "x");

        _access.AppSettings = new AccessAppSettings("GESCONS", "LDAP://OU=Accounts,DC=GESCONS,DC=RU", "GESCONS", "access",
            Secret, "https://mail.gescons.ru/ews", "Fired Users", true);
        _access.ExcludedOus.AddRange(["IT", "Others"]);
        _access.TerminatedOus.Add(new("Fired_Users", "OU=Fired_Users,DC=gescons,DC=ru", "Уволенные", true));
        _access.TerminatedOus.Add(new("Old", null, null, false));
        _access.RolePrincipals.Add(new("super_admin", "group", "PortalAdmins", _admins.DistinguishedName!, "PortalAdmins"));
        _access.RolePrincipals.Add(new("admin", "user", "ivanov", _ivanov.DistinguishedName!.ToUpperInvariant(), "Иванов"));
        _access.RolePrincipals.Add(new("hr", "group", "HR", _hr.DistinguishedName!, "HR"));
        _access.RolePrincipals.Add(new("hr", "group", "Gone", "CN=Gone,DC=test,DC=local", "Gone"));
        _access.PortalUsers.Add(new("petrov", "hr", true));
        _access.PortalUsers.Add(new("sidorov", "admin", false));
        _access.LocalUsers.Add(new("admin", "super_admin", true));
        _access.Extras = new AccessExtras(5, 3);
    }

    public void Dispose()
    {
        _sp.Dispose();
        _connection.Dispose();
    }

    private async Task<AccessImportReport> RunAsync(AccessImportOptions options)
    {
        using var scope = _sp.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AccessImporter>().RunAsync(options);
    }

    private T Get<T>() where T : notnull => _sp.GetRequiredService<T>();

    private async Task<List<(string Role, string Type, string Sid)>> AssignmentsAsync()
    {
        using var scope = _sp.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<IRoleService>().ListAssignmentsAsync())
            .Select(a => (a.RoleName, a.PrincipalType.ToString(), a.PrincipalId)).ToList();
    }

    private List<WinAdmin.Infrastructure.Storage.AuditEntryEntity> Audit()
    {
        using var scope = _sp.CreateScope();
        return scope.ServiceProvider.GetRequiredService<WinAdminDbContext>().AuditEntries.AsNoTracking().OrderBy(a => a.Id).ToList();
    }

    private static ImportRow Row(AccessImportReport report, string what)
        => Assert.Single(report.Rows, r => r.What.Contains(what, StringComparison.Ordinal));

    [Fact]
    public async Task Dry_run_reports_everything_and_changes_nothing()
    {
        _access.Audit.Add(new(1, DateTimeOffset.UtcNow, "ivanov", "user.move", "ad_user", "petrov", null));
        var report = await RunAsync(new AccessImportOptions());

        Assert.False(report.Applied);
        Assert.Equal(ImportStatus.WillCreate, Row(report, "Корневая OU").Status);
        Assert.Equal(ImportStatus.WillCreate, Row(report, "Учётка записи").Status);
        Assert.Equal(ImportStatus.NotImported, Row(report, "Пароль учётки записи").Status);
        Assert.Equal(ImportStatus.WillCreate, Row(report, "Группа уволенных").Status);
        Assert.Equal(ImportStatus.WillCreate, Row(report, "OU уволенных: Fired_Users").Status);
        Assert.Equal(ImportStatus.NotImported, Row(report, "OU уволенных: Old").Status);
        Assert.Equal(ImportStatus.WillCreate, Row(report, "Скрытая OU: IT").Status);
        Assert.Equal(ImportStatus.WillCreate, Row(report, $"Роль-шаблон «{AdUsersModule.HrRoleName}»").Status);
        Assert.Equal(ImportStatus.NotFoundInAd, Row(report, "Gone").Status);
        Assert.Equal(2, report.Rows.Count(r => r.What.Contains("Иванов") && r.Status == ImportStatus.WillCreate));
        Assert.Equal(ImportStatus.NotImported, Row(report, "sidorov").Status);
        Assert.Equal(ImportStatus.NotImported, Row(report, "Локальный пользователь admin").Status);
        Assert.Contains("user add", Row(report, "Локальный пользователь admin").Note);
        Assert.Equal(ImportStatus.NotImported, Row(report, "Метаданные проектов").Status);
        Assert.Equal(ImportStatus.NotImported, Row(report, "Exchange").Status);
        Assert.Equal(ImportStatus.NotImported, Row(report, "Журнал аудита").Status);
        Assert.Contains(report.Hints, h => h.Contains("Пользователи AD") && h.Contains("Папки"));
        Assert.Contains(report.Hints, h => h.Contains("portal_users") && h.Contains("суммируются"));

        Assert.Null((await Get<IAdStructureStore>().GetAsync()).RootOu);
        Assert.Empty(await AssignmentsAsync());
        using var scope = _sp.CreateScope();
        Assert.Single(await scope.ServiceProvider.GetRequiredService<IRoleService>().ListAsync());
        Assert.Empty(Audit());
    }

    [Fact]
    public async Task Apply_imports_settings_and_roles_and_second_run_only_skips()
    {
        await Get<IAdStructureStore>().SaveAsync(AdStructureSettings.Default with { HiddenOus = ["Others", "Service"] }, null);

        var report = await RunAsync(new AccessImportOptions(Apply: true));

        Assert.True(report.Applied);
        Assert.DoesNotContain(report.Rows, r => r.Status == ImportStatus.WillCreate);
        var st = await Get<IAdStructureStore>().GetAsync();
        Assert.Equal("OU=Accounts,DC=GESCONS,DC=RU", st.RootOu);
        Assert.Equal(@"GESCONS\access", st.WriteLogin);
        Assert.False(st.HasWritePassword);
        Assert.Equal(["Others", "Service", "IT"], st.HiddenOus);
        Assert.Equal(ImportStatus.Exists, Row(report, "Скрытая OU: Others").Status);
        var users = await Get<IModuleRegistry>().GetSettingsAsync<AdUsersSettings>(AdUsersModule.ModuleId);
        Assert.Equal("Fired Users", users.FiredGroup);
        Assert.Equal("OU=Fired_Users,DC=gescons,DC=ru", users.TerminatedOuDn);
        Assert.False(Get<IModuleRegistry>().GetState(AdUsersModule.ModuleId).Enabled);

        var assignments = await AssignmentsAsync();
        Assert.Equal(
            new[]
            {
                (AdFoldersModule.AdminRoleName, "AdUser", _ivanov.Sid),
                (AdUsersModule.AdminRoleName, "AdUser", _ivanov.Sid),
                (AdUsersModule.HrRoleName, "AdGroup", _hr.Sid),
                (AdUsersModule.HrRoleName, "AdUser", _petrov.Sid),
                (BuiltInRoles.AdministratorName, "AdGroup", _admins.Sid),
            }.Order(),
            assignments.Order());
        var summary = Assert.Single(Audit(), a => a.Action == "import.access");
        Assert.Contains("Создано", summary.Details);

        var again = await RunAsync(new AccessImportOptions(Apply: true));
        Assert.DoesNotContain(again.Rows, r => r.Status is ImportStatus.Created or ImportStatus.WillCreate or ImportStatus.Conflict);
        Assert.Equal(5, (await AssignmentsAsync()).Count);
        using var scope = _sp.CreateScope();
        Assert.Equal(4, (await scope.ServiceProvider.GetRequiredService<IRoleService>().ListAsync()).Count);
    }

    [Fact]
    public async Task Different_values_are_conflicts_unless_overwrite()
    {
        await Get<IAdStructureStore>().SaveAsync(AdStructureSettings.Default with { RootOu = "OU=Corp,DC=gescons,DC=ru", WriteLogin = @"GESCONS\svc" }, null);
        await Get<IModuleRegistry>().SaveSettingsAsync(AdUsersModule.ModuleId,
            new System.Text.Json.Nodes.JsonObject { ["firedGroup"] = "Ex-Staff", ["terminatedOuDn"] = "OU=Gone,DC=gescons,DC=ru" }, "test");

        var report = await RunAsync(new AccessImportOptions(Apply: true));
        Assert.Equal(ImportStatus.Conflict, Row(report, "Корневая OU").Status);
        Assert.Equal(ImportStatus.Conflict, Row(report, "Учётка записи").Status);
        Assert.Equal(ImportStatus.Conflict, Row(report, "Группа уволенных").Status);
        Assert.Equal(ImportStatus.Conflict, Row(report, "OU уволенных: Fired_Users").Status);
        Assert.Equal("OU=Corp,DC=gescons,DC=ru", (await Get<IAdStructureStore>().GetAsync()).RootOu);
        Assert.Equal("Ex-Staff", (await Get<IModuleRegistry>().GetSettingsAsync<AdUsersSettings>(AdUsersModule.ModuleId)).FiredGroup);

        var overwritten = await RunAsync(new AccessImportOptions(Apply: true, Overwrite: true));
        Assert.Equal(ImportStatus.Created, Row(overwritten, "Корневая OU").Status);
        var st = await Get<IAdStructureStore>().GetAsync();
        Assert.Equal("OU=Accounts,DC=GESCONS,DC=RU", st.RootOu);
        Assert.Equal(@"GESCONS\access", st.WriteLogin);
        var users = await Get<IModuleRegistry>().GetSettingsAsync<AdUsersSettings>(AdUsersModule.ModuleId);
        Assert.Equal("Fired Users", users.FiredGroup);
        Assert.Equal("OU=Fired_Users,DC=gescons,DC=ru", users.TerminatedOuDn);
    }

    [Fact]
    public async Task Same_values_in_other_form_are_not_conflicts()
    {
        await Get<IAdStructureStore>().SaveAsync(AdStructureSettings.Default with
            { RootOu = "ou=accounts, dc=gescons, dc=ru", WriteLogin = "access@gescons.ru" }, null);

        var report = await RunAsync(new AccessImportOptions());

        Assert.Equal(ImportStatus.Exists, Row(report, "Корневая OU").Status);
        Assert.Equal(ImportStatus.Exists, Row(report, "Учётка записи").Status);
    }

    [Fact]
    public async Task Service_password_moves_only_with_flag_encrypted_and_never_printed()
    {
        var withoutFlag = await RunAsync(new AccessImportOptions(Apply: true));
        Assert.Null((await Get<IAdStructureStore>().GetWriteCredentialAsync()).Password);
        Assert.Contains("--import-service-password", Row(withoutFlag, "Пароль учётки записи").Note);

        var report = await RunAsync(new AccessImportOptions(Apply: true, ImportServicePassword: true));

        Assert.Equal(ImportStatus.Created, Row(report, "Пароль учётки записи").Status);
        Assert.Equal(Secret, (await Get<IAdStructureStore>().GetWriteCredentialAsync()).Password);
        using (var scope = _sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WinAdminDbContext>();
            Assert.DoesNotContain(Secret, db.PlatformSettings.Single(s => s.Key == AdStructureStore.Key).Json);
            Assert.DoesNotContain(db.AuditEntries.ToList(), a => (a.Details ?? "").Contains(Secret) || (a.Target ?? "").Contains(Secret));
        }
        Assert.DoesNotContain(report.Rows, r => $"{r.What}{r.From}{r.To}{r.Note}".Contains(Secret));
        Assert.DoesNotContain(Secret, _access.AppSettings!.ToString());

        var again = await RunAsync(new AccessImportOptions(Apply: true, ImportServicePassword: true));
        Assert.Equal(ImportStatus.Exists, Row(again, "Пароль учётки записи").Status);
    }

    [Fact]
    public async Task Service_password_is_not_attached_to_another_account()
    {
        await Get<IAdStructureStore>().SaveAsync(AdStructureSettings.Default with { WriteLogin = @"GESCONS\svc" }, "other");

        var report = await RunAsync(new AccessImportOptions(Apply: true, ImportServicePassword: true));

        Assert.Equal(ImportStatus.NotImported, Row(report, "Пароль учётки записи").Status);
        Assert.Equal("other", (await Get<IAdStructureStore>().GetWriteCredentialAsync()).Password);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ЗАМЕНИТЕ_НА_ПАРОЛЬ")]
    public async Task Empty_or_placeholder_password_is_not_imported(string password)
    {
        _access.AppSettings = _access.AppSettings! with { ServicePassword = password };
        var report = await RunAsync(new AccessImportOptions(Apply: true, ImportServicePassword: true));
        Assert.Equal(ImportStatus.NotImported, Row(report, "Пароль учётки записи").Status);
        Assert.False((await Get<IAdStructureStore>().GetAsync()).HasWritePassword);
    }

    [Theory]
    [InlineData("LDAP://OU=Accounts,DC=GESCONS,DC=RU", "OU=Accounts,DC=GESCONS,DC=RU")]
    [InlineData("LDAP://dc01.gescons.ru/OU=Accounts,DC=gescons,DC=ru", "OU=Accounts,DC=gescons,DC=ru")]
    [InlineData("ou=Accounts , dc=gescons,dc=ru", "OU=Accounts,DC=gescons,DC=ru")]
    [InlineData("  ", null)]
    public void Ldap_path_is_parsed_to_dn(string ldapPath, string? dn)
        => Assert.Equal(dn, AccessImporter.ParseLdapPath(ldapPath));

    [Theory]
    [InlineData("LDAP://DC=GESCONS,DC=RU")]
    [InlineData("LDAP://OU=Accounts")]
    public async Task Ldap_path_that_is_not_an_ou_dn_is_not_imported(string ldapPath)
    {
        _access.AppSettings = _access.AppSettings! with { LdapPath = ldapPath };
        var report = await RunAsync(new AccessImportOptions(Apply: true));
        Assert.Equal(ImportStatus.NotImported, Row(report, "Корневая OU").Status);
        Assert.Null((await Get<IAdStructureStore>().GetAsync()).RootOu);
    }

    [Fact]
    public async Task Unknown_access_role_is_not_imported()
    {
        _access.RolePrincipals.Add(new("auditor", "user", "ivanov", _ivanov.DistinguishedName!, "Иванов"));
        var report = await RunAsync(new AccessImportOptions());
        Assert.Contains(report.Rows, r => r.What.Contains("auditor") && r.Status == ImportStatus.NotImported);
    }

    [Fact]
    public async Task Unavailable_directory_stops_before_any_change()
    {
        _directory.Down = true;
        await Assert.ThrowsAsync<DirectoryUnavailableException>(() => RunAsync(new AccessImportOptions(Apply: true)));
        Assert.Null((await Get<IAdStructureStore>().GetAsync()).RootOu);
        Assert.Empty(Audit());
    }

    [Fact]
    public async Task Audit_moves_only_with_flag_incrementally_and_without_passwords()
    {
        var t0 = new DateTimeOffset(2025, 3, 1, 10, 0, 0, TimeSpan.Zero);
        _access.Audit.Add(new(1, t0, "ivanov", "user.password.reset", "ad_user", "petrov", null));
        _access.Audit.Add(new(2, t0.AddMinutes(1), "ivanov", "user.account.activate", "ad_user", "petrov",
            """{"project":"P1","newPassword":"Qwerty123!","nested":{"Password":"x","keep":1}}"""));
        _access.Audit.Add(new(3, t0.AddMinutes(2), "hr1", "settings.update", null, null, null));

        await RunAsync(new AccessImportOptions(Apply: true));
        Assert.DoesNotContain(Audit(), a => a.Action.StartsWith("access.", StringComparison.Ordinal));

        var report = await RunAsync(new AccessImportOptions(Apply: true, Audit: true));
        Assert.Equal(ImportStatus.Created, Row(report, "Журнал аудита").Status);
        var imported = Audit().Where(a => a.Action.StartsWith("access.", StringComparison.Ordinal)).ToList();
        Assert.Equal(["access.user.password.reset", "access.user.account.activate", "access.settings.update"], imported.Select(a => a.Action));
        var activate = imported[1];
        Assert.Equal(t0.AddMinutes(1), activate.Timestamp);
        Assert.Equal("ivanov", activate.Actor);
        Assert.Equal("petrov", activate.Target);
        Assert.True(activate.Success);
        Assert.Contains("P1", activate.Details);
        Assert.Contains("ad_user", activate.Details);
        Assert.DoesNotContain("Qwerty123!", activate.Details);
        Assert.DoesNotContain("assword", activate.Details);

        _access.Audit.Add(new(4, t0.AddMinutes(3), "hr1", "user.move", "ad_user", "petrov", null));
        var dry = await RunAsync(new AccessImportOptions(Audit: true));
        Assert.Contains("1", Row(dry, "Журнал аудита").From);
        await RunAsync(new AccessImportOptions(Apply: true, Audit: true));
        Assert.Equal(4, Audit().Count(a => a.Action.StartsWith("access.", StringComparison.Ordinal)));
        var last = await RunAsync(new AccessImportOptions(Apply: true, Audit: true));
        Assert.Equal(ImportStatus.Exists, Row(last, "Журнал аудита").Status);
        Assert.Equal(4, Audit().Count(a => a.Action.StartsWith("access.", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Missing_app_settings_row_is_reported_not_failed()
    {
        _access.AppSettings = null;
        var report = await RunAsync(new AccessImportOptions(Apply: true));
        Assert.Contains(report.Rows, r => r.What.Contains("app_settings") && r.Status == ImportStatus.NotImported);
    }
}
