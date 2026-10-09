using Microsoft.EntityFrameworkCore;
using Npgsql;
using WinAdmin.Core.Models;
using WinAdmin.Infrastructure.Security;
using WinAdmin.Infrastructure.Settings;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Tests;

public sealed class PostgresStorageTests : IDisposable
{
    private readonly string _cs;
    private readonly PostgresWinAdminDbContext _db;

    public PostgresStorageTests()
    {
        var b = new NpgsqlConnectionStringBuilder(PostgresFactAttribute.ConnectionString ?? "Host=unused")
        {
            Database = "winadmin_test_" + Guid.NewGuid().ToString("N")[..12],
        };
        _cs = b.ConnectionString;
        _db = new PostgresWinAdminDbContext(new DbContextOptionsBuilder<PostgresWinAdminDbContext>().UseNpgsql(_cs).Options);
    }

    public void Dispose()
    {
        if (PostgresFactAttribute.ConnectionString is not null)
            _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    [PostgresFact]
    public async Task Migrations_create_database_and_audit_sorts_newest_first()
    {
        await _db.Database.MigrateAsync();
        var audit = new AuditService(_db);
        await audit.WriteAsync(new AuditEntryDto { Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5), Actor = "a", Action = "old", Success = true });
        await audit.WriteAsync(new AuditEntryDto { Timestamp = DateTimeOffset.UtcNow, Actor = "b", Action = "new", Success = true });

        var entries = await audit.QueryAsync();
        Assert.Equal("new", entries[0].Action);
        Assert.Empty(await _db.Database.GetPendingMigrationsAsync());
    }

    [PostgresFact]
    public async Task Unique_violation_is_detected_like_on_sqlite()
    {
        await _db.Database.MigrateAsync();
        var excluded = new ExcludedUserService(_db);
        await excluded.AddAsync("svc_backup");
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => excluded.AddAsync("svc_backup"));
        // Контроллер распознаёт дубль по слову unique/UNIQUE в сообщении (ExcludedUsersController).
        string all = ex.ToString();
        Assert.True(all.Contains("UNIQUE") || all.Contains("unique"), all);
    }
}
