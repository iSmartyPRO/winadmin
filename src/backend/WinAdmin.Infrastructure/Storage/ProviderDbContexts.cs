using Microsoft.EntityFrameworkCore;

namespace WinAdmin.Infrastructure.Storage;

/// <summary>SQLite: миграции в Storage/Migrations/Sqlite.</summary>
public sealed class SqliteWinAdminDbContext(DbContextOptions<SqliteWinAdminDbContext> options)
    : WinAdminDbContext(options);

/// <summary>PostgreSQL: миграции в Storage/Migrations/PostgreSql.</summary>
public sealed class PostgresWinAdminDbContext(DbContextOptions<PostgresWinAdminDbContext> options)
    : WinAdminDbContext(options);
