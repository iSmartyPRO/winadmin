using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WinAdmin.Infrastructure.Storage;

/// <summary>Только для dotnet ef (без стартовой логики API).</summary>
public sealed class SqliteDesignTimeDbContextFactory : IDesignTimeDbContextFactory<SqliteWinAdminDbContext>
{
    public SqliteWinAdminDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<SqliteWinAdminDbContext>().UseSqlite("Data Source=WinAdmin-design.db").Options);
}

/// <summary>Только для dotnet ef; генерация миграций не требует работающего сервера.</summary>
public sealed class PostgresDesignTimeDbContextFactory : IDesignTimeDbContextFactory<PostgresWinAdminDbContext>
{
    public PostgresWinAdminDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<PostgresWinAdminDbContext>().UseNpgsql("Host=localhost;Database=winadmin_design").Options);
}
