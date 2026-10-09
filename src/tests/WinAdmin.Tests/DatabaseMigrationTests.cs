using Microsoft.EntityFrameworkCore;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Tests;

public sealed class DatabaseMigrationTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "winadmin-mig-" + Guid.NewGuid().ToString("N") + ".db");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_file)) File.Delete(_file);
    }

    private SqliteWinAdminDbContext Sqlite() => new(
        new DbContextOptionsBuilder<SqliteWinAdminDbContext>().UseSqlite($"Data Source={_file}").Options);

    [Fact]
    public void Existing_sqlite_migration_ids_are_kept_so_upgrades_see_them_as_applied()
    {
        using (var db = Sqlite()) db.Database.Migrate();

        using var again = Sqlite();
        Assert.Equal(
            new[] { "20260623120856_InitialCreate", "20260711023114_AddUsers", "20260712061045_AddExcludedUsers" },
            again.Database.GetAppliedMigrations().Take(3));
        Assert.Empty(again.Database.GetPendingMigrations());
    }

    [Fact]
    public void Sqlite_model_matches_its_migrations()
    {
        using var db = Sqlite();
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public void Postgres_model_matches_its_migrations()
    {
        using var db = new PostgresWinAdminDbContext(new DbContextOptionsBuilder<PostgresWinAdminDbContext>()
            .UseNpgsql("Host=localhost;Database=unused").Options);
        Assert.NotEmpty(db.Database.GetMigrations());
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
