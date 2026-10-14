using Npgsql;
using WinAdmin.Core.Import;

namespace WinAdmin.Infrastructure.Import;

/// <summary>
/// Чтение БД портала Access (только SELECT). Схема любой версии v1…v11: отсутствующие таблицы
/// (role_principals, project_metadata) и колонки (fired_users_group, ou_dn, …) читаются как пустые.
/// </summary>
public sealed class NpgsqlAccessSource(string connectionString) : IAccessSource
{
    private readonly Dictionary<string, HashSet<string>> _columns = new(StringComparer.Ordinal);

    public string Description
    {
        get
        {
            var b = new NpgsqlConnectionStringBuilder(connectionString);
            return $"PostgreSQL {b.Host}:{b.Port}/{b.Database}";
        }
    }

    public async Task<AccessAppSettings?> GetAppSettingsAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        var cols = await ColumnsAsync(conn, "app_settings", ct);
        if (cols.Count == 0) return null;
        string Text(string c) => cols.Contains(c) ? $"COALESCE({c}, '')" : "''";
        string api = (cols.Contains("api_settings") ? "(COALESCE(api_settings::text, '{}') NOT IN ('{}', 'null'))" : "FALSE")
                     + (cols.Contains("yandex_maps_api_key") ? " OR COALESCE(TRIM(yandex_maps_api_key), '') <> ''" : "");
        await using var cmd = new NpgsqlCommand($"""
            SELECT {Text("domain")}, {Text("ldap_path")}, {Text("ad_service_domain")}, {Text("ad_service_username")},
                   {Text("ad_service_password")}, {Text("exchange_url")}, {Text("fired_users_group")}, ({api})
            FROM app_settings WHERE id = 1
            """, conn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new AccessAppSettings(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4),
            r.GetString(5), r.GetString(6), r.GetBoolean(7));
    }

    public async Task<IReadOnlyList<string>> GetExcludedOusAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        if ((await ColumnsAsync(conn, "excluded_ous", ct)).Count == 0) return [];
        return await ListAsync(conn, "SELECT ou_name FROM excluded_ous ORDER BY ou_name", r => r.GetString(0), ct);
    }

    public async Task<IReadOnlyList<AccessTerminatedOu>> GetTerminatedOusAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        var cols = await ColumnsAsync(conn, "terminated_user_ous", ct);
        if (cols.Count == 0) return [];
        string Opt(string c, string fallback) => cols.Contains(c) ? c : fallback;
        return await ListAsync(conn, $"""
            SELECT ou_name, {Opt("ou_dn", "NULL::text")}, {Opt("display_name", "NULL::text")}, {Opt("is_deactivation_default", "FALSE")}
            FROM terminated_user_ous ORDER BY ou_name
            """,
            r => new AccessTerminatedOu(r.GetString(0), NullableString(r, 1), NullableString(r, 2), !r.IsDBNull(3) && r.GetBoolean(3)), ct);
    }

    public async Task<IReadOnlyList<AccessRolePrincipal>> GetRolePrincipalsAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        if ((await ColumnsAsync(conn, "role_principals", ct)).Count == 0) return [];
        return await ListAsync(conn, """
            SELECT r.code, rp.principal_type, rp.sam_account_name, rp.distinguished_name, rp.display_name
            FROM role_principals rp JOIN roles r ON r.id = rp.role_id
            ORDER BY r.id, rp.id
            """,
            r => new AccessRolePrincipal(r.GetString(0), r.GetString(1).Trim().ToLowerInvariant(), NullableString(r, 2), r.GetString(3), r.GetString(4)), ct);
    }

    public async Task<IReadOnlyList<AccessPortalUser>> GetPortalUsersAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        if ((await ColumnsAsync(conn, "portal_users", ct)).Count == 0) return [];
        return await ListAsync(conn, """
            SELECT pu.sam_account_name, r.code, pu.is_active
            FROM portal_users pu JOIN roles r ON r.id = pu.role_id ORDER BY pu.id
            """,
            r => new AccessPortalUser(r.GetString(0), r.GetString(1), r.GetBoolean(2)), ct);
    }

    public async Task<IReadOnlyList<AccessLocalUser>> GetLocalUsersAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        if ((await ColumnsAsync(conn, "local_users", ct)).Count == 0) return [];
        // password_hash не читается: не переносится.
        return await ListAsync(conn, """
            SELECT lu.username, r.code, lu.is_active
            FROM local_users lu JOIN roles r ON r.id = lu.role_id ORDER BY lu.id
            """,
            r => new AccessLocalUser(r.GetString(0), r.GetString(1), r.GetBoolean(2)), ct);
    }

    public async Task<AccessExtras> GetExtrasAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        var cols = await ColumnsAsync(conn, "project_metadata", ct);
        if (cols.Count == 0) return new AccessExtras(0, 0);
        string mail = cols.Contains("mail_group_dn") ? "COUNT(*) FILTER (WHERE COALESCE(mail_group_dn, '') <> '')" : "0";
        await using var cmd = new NpgsqlCommand($"SELECT COUNT(*)::int, ({mail})::int FROM project_metadata", conn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        await r.ReadAsync(ct);
        return new AccessExtras(r.GetInt32(0), r.GetInt32(1));
    }

    public async Task<long> CountAuditAsync(long afterId, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        if ((await ColumnsAsync(conn, "audit_log", ct)).Count == 0) return 0;
        await using var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM audit_log WHERE id > @after", conn);
        cmd.Parameters.AddWithValue("after", afterId);
        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task<IReadOnlyList<AccessAuditEntry>> ReadAuditAsync(long afterId, int limit, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        if ((await ColumnsAsync(conn, "audit_log", ct)).Count == 0) return [];
        await using var cmd = new NpgsqlCommand("""
            SELECT id, created_at, actor_sam, action, target_type, target_id, details::text
            FROM audit_log WHERE id > @after ORDER BY id LIMIT @limit
            """, conn);
        cmd.Parameters.AddWithValue("after", afterId);
        cmd.Parameters.AddWithValue("limit", limit);
        var list = new List<AccessAuditEntry>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new AccessAuditEntry(r.GetInt64(0), r.GetFieldValue<DateTimeOffset>(1), r.GetString(2), r.GetString(3),
                NullableString(r, 4), NullableString(r, 5), NullableString(r, 6)));
        return list;
    }

    // ── помощники ────────────────────────────────────────────────

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    /// <summary>Колонки таблицы в текущей схеме; пусто — таблицы нет.</summary>
    private async Task<HashSet<string>> ColumnsAsync(NpgsqlConnection conn, string table, CancellationToken ct)
    {
        if (_columns.TryGetValue(table, out var cached)) return cached;
        var set = new HashSet<string>(StringComparer.Ordinal);
        await using var cmd = new NpgsqlCommand(
            "SELECT column_name FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = @t", conn);
        cmd.Parameters.AddWithValue("t", table);
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                set.Add(r.GetString(0));
        return _columns[table] = set;
    }

    private static async Task<IReadOnlyList<T>> ListAsync<T>(NpgsqlConnection conn, string sql, Func<NpgsqlDataReader, T> map, CancellationToken ct)
    {
        var list = new List<T>();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(map(r));
        return list;
    }

    private static string? NullableString(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
}
