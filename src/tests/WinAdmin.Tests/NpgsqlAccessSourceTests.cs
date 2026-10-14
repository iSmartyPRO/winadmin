using Npgsql;
using WinAdmin.Core.Import;
using WinAdmin.Infrastructure.Import;

namespace WinAdmin.Tests;

/// <summary>
/// Чтение настоящей БД Access: временная БД access_import_test_*, к ней schema.sql и миграции Access
/// (копии в Resources/AccessSchema), строки, чтение NpgsqlAccessSource, удаление БД.
/// </summary>
public sealed class NpgsqlAccessSourceTests : IDisposable
{
    private readonly string _database = "access_import_test_" + Guid.NewGuid().ToString("N");
    private readonly string _cs;
    private bool _created;

    public NpgsqlAccessSourceTests()
        => _cs = new NpgsqlConnectionStringBuilder(PostgresFactAttribute.ConnectionString ?? "Host=unused") { Database = _database }.ConnectionString;

    public void Dispose()
    {
        if (!_created) return;
        NpgsqlConnection.ClearAllPools();
        using var conn = new NpgsqlConnection(PostgresFactAttribute.ConnectionString);
        conn.Open();
        using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)", conn);
        cmd.ExecuteNonQuery();
    }

    private async Task CreateAsync(bool migrations)
    {
        await using (var admin = new NpgsqlConnection(PostgresFactAttribute.ConnectionString))
        {
            await admin.OpenAsync();
            await new NpgsqlCommand($"CREATE DATABASE \"{_database}\"", admin).ExecuteNonQueryAsync();
            _created = true;
        }
        string dir = Path.Combine(AppContext.BaseDirectory, "Resources", "AccessSchema");
        var files = new List<string> { "schema.sql" };
        if (migrations)
            files.AddRange(Directory.GetFiles(dir, "migrate_v*.sql").Select(Path.GetFileName)
                .OrderBy(f => int.Parse(f!["migrate_v".Length..].Split('_', '.')[0])).Cast<string>());
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync();
        foreach (var file in files)
            await new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(dir, file)), conn).ExecuteNonQueryAsync();
    }

    private async Task ExecAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync();
        await new NpgsqlCommand(sql, conn).ExecuteNonQueryAsync();
    }

    [PostgresFact]
    public async Task Reads_migrated_access_database()
    {
        await CreateAsync(migrations: true);
        await ExecAsync("""
            INSERT INTO roles (code, name) VALUES ('super_admin', 'Супер админ'), ('admin', 'Администраторы'), ('hr', 'Отдел кадров');
            UPDATE app_settings SET ad_service_password = 'pw-from-test', exchange_url = 'https://mail/ews',
                fired_users_group = 'Fired Users', api_settings = '{"yandexMaps":{"apiKey":"k"}}' WHERE id = 1;
            INSERT INTO excluded_ous (ou_name) VALUES ('IT'), ('Others');
            INSERT INTO terminated_user_ous (ou_name, ou_dn, display_name, is_deactivation_default)
                VALUES ('Fired_Users', 'OU=Fired_Users,DC=gescons,DC=ru', 'Уволенные', TRUE), ('Old', NULL, NULL, FALSE);
            INSERT INTO role_principals (role_id, principal_type, sam_account_name, distinguished_name, display_name)
                SELECT id, 'group', 'PortalAdmins', 'CN=PortalAdmins,OU=Groups,DC=gescons,DC=ru', 'PortalAdmins' FROM roles WHERE code = 'super_admin';
            INSERT INTO role_principals (role_id, principal_type, sam_account_name, distinguished_name, display_name)
                SELECT id, 'user', NULL, 'CN=Иванов Иван,OU=Users,DC=gescons,DC=ru', 'Иванов' FROM roles WHERE code = 'hr';
            INSERT INTO portal_users (sam_account_name, role_id, is_active) SELECT 'petrov', id, TRUE FROM roles WHERE code = 'admin';
            INSERT INTO portal_users (sam_account_name, role_id, is_active) SELECT 'sidorov', id, FALSE FROM roles WHERE code = 'hr';
            INSERT INTO local_users (username, password_hash, role_id) SELECT 'admin', 'pbkdf2-sha256|1|x|y', id FROM roles WHERE code = 'super_admin';
            INSERT INTO project_metadata (project_name, mail_group_dn) VALUES ('P1', 'CN=Mail P1,OU=Groups,DC=gescons,DC=ru'), ('P2', NULL);
            INSERT INTO audit_log (actor_sam, action, target_type, target_id, details, created_at) VALUES
                ('ivanov', 'user.move', 'ad_user', 'petrov', '{"from":"P1","to":"P2"}', '2025-03-01T10:00:00Z'),
                ('ivanov', 'settings.update', NULL, NULL, NULL, '2025-03-01T11:00:00Z'),
                ('hr1', 'user.password.reset', 'ad_user', 'petrov', NULL, '2025-03-02T09:30:00Z');
            """);

        var source = new NpgsqlAccessSource(_cs);

        Assert.Contains(_database, source.Description);
        Assert.DoesNotContain("pw-from-test", source.Description);
        var app = (await source.GetAppSettingsAsync())!;
        Assert.Equal("LDAP://OU=Accounts,DC=GESCONS,DC=RU", app.LdapPath);
        Assert.Equal("GESCONS", app.ServiceDomain);
        Assert.Equal("access", app.ServiceUsername);
        Assert.Equal("pw-from-test", app.ServicePassword);
        Assert.Equal("Fired Users", app.FiredUsersGroup);
        Assert.Equal("https://mail/ews", app.ExchangeUrl);
        Assert.True(app.HasApiSettings);
        Assert.Equal(["IT", "Others"], await source.GetExcludedOusAsync());
        var terminated = await source.GetTerminatedOusAsync();
        Assert.Equal(new AccessTerminatedOu("Fired_Users", "OU=Fired_Users,DC=gescons,DC=ru", "Уволенные", true), terminated[0]);
        Assert.Equal(new AccessTerminatedOu("Old", null, null, false), terminated[1]);
        var principals = await source.GetRolePrincipalsAsync();
        Assert.Equal(2, principals.Count);
        Assert.Contains(new AccessRolePrincipal("hr", "user", null, "CN=Иванов Иван,OU=Users,DC=gescons,DC=ru", "Иванов"), principals);
        Assert.Equal([new AccessPortalUser("petrov", "admin", true), new AccessPortalUser("sidorov", "hr", false)],
            (await source.GetPortalUsersAsync()).OrderBy(u => u.SamAccountName));
        Assert.Equal([new AccessLocalUser("admin", "super_admin", true)], await source.GetLocalUsersAsync());
        Assert.Equal(new AccessExtras(2, 1), await source.GetExtrasAsync());

        Assert.Equal(3, await source.CountAuditAsync(0));
        var first = await source.ReadAuditAsync(0, 2);
        Assert.Equal(2, first.Count);
        Assert.Equal(new DateTimeOffset(2025, 3, 1, 10, 0, 0, TimeSpan.Zero), first[0].CreatedAt);
        Assert.Equal(("ivanov", "user.move", "ad_user", "petrov"), (first[0].ActorSam, first[0].Action, first[0].TargetType, first[0].TargetId));
        Assert.Contains("\"from\"", first[0].DetailsJson);
        Assert.Null(first[1].DetailsJson);
        var rest = await source.ReadAuditAsync(first[1].Id, 10);
        Assert.Equal("user.password.reset", Assert.Single(rest).Action);
        Assert.Equal(1, await source.CountAuditAsync(first[1].Id));
    }

    [PostgresFact]
    public async Task Old_schema_without_migrations_reads_as_empty()
    {
        await CreateAsync(migrations: false);
        await ExecAsync("""
            ALTER TABLE app_settings DROP COLUMN fired_users_group, DROP COLUMN api_settings, DROP COLUMN exchange_url,
                DROP COLUMN yandex_maps_api_key;
            ALTER TABLE terminated_user_ous DROP COLUMN is_deactivation_default, DROP COLUMN display_name, DROP COLUMN ou_dn;
            DROP TABLE project_metadata;
            INSERT INTO app_settings (id, ldap_path) VALUES (1, 'LDAP://OU=A,DC=x');
            INSERT INTO terminated_user_ous (ou_name) VALUES ('Fired');
            """);

        var source = new NpgsqlAccessSource(_cs);

        Assert.Equal("LDAP://OU=A,DC=x", (await source.GetAppSettingsAsync())!.LdapPath);
        Assert.Equal("", (await source.GetAppSettingsAsync())!.FiredUsersGroup);
        Assert.Equal([new AccessTerminatedOu("Fired", null, null, false)], await source.GetTerminatedOusAsync());
        Assert.Equal(new AccessExtras(0, 0), await source.GetExtrasAsync());
        Assert.Empty(await source.GetRolePrincipalsAsync());
        Assert.Empty(await source.GetPortalUsersAsync());
        Assert.Equal(0, await source.CountAuditAsync(0));
    }
}
