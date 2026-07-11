using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WinAdmin.Core.Models;
using WinAdmin.Infrastructure.Security;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Tests;

public sealed class AuditServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WinAdminDbContext _db;
    private readonly AuditService _service;

    public AuditServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<WinAdminDbContext>().UseSqlite(_connection).Options;
        _db = new WinAdminDbContext(options);
        _db.Database.EnsureCreated();
        _service = new AuditService(_db);
    }

    [Fact]
    public async Task Writes_and_queries_newest_first()
    {
        // Запросы по DateTimeOffset должны работать в SQLite (хранятся как тики).
        await _service.WriteAsync(new AuditEntryDto
        {
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5),
            Actor = "k1", Action = "power.reboot", Target = "WS-01", Success = true,
        });
        await _service.WriteAsync(new AuditEntryDto
        {
            Timestamp = DateTimeOffset.UtcNow,
            Actor = "k2", Action = "service.stop", Target = "Spooler", Success = false,
        });

        var entries = await _service.QueryAsync();
        Assert.Equal(2, entries.Count);
        Assert.Equal("service.stop", entries[0].Action); // новее — первым
        Assert.Equal("power.reboot", entries[1].Action);
    }

    [Fact]
    public async Task Filters_by_actor()
    {
        await _service.WriteAsync(new AuditEntryDto { Actor = "a", Action = "x", Success = true });
        await _service.WriteAsync(new AuditEntryDto { Actor = "b", Action = "y", Success = true });

        var onlyA = await _service.QueryAsync(actor: "a");
        Assert.Single(onlyA);
        Assert.Equal("a", onlyA[0].Actor);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
