using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace WinAdmin.Infrastructure.Storage;

public enum DatabaseProvider { Sqlite, PostgreSql }

/// <summary>Провайдер и строка подключения (для использования — с расшифрованным паролем).</summary>
public sealed record DatabaseSettings(DatabaseProvider Provider, string ConnectionString);

public static class DatabaseServiceCollectionExtensions
{
    /// <summary>Регистрирует WinAdminDbContext с реализацией под выбранного провайдера.</summary>
    public static IServiceCollection AddWinAdminDatabase(this IServiceCollection services, DatabaseSettings database)
        => database.Provider switch
        {
            DatabaseProvider.Sqlite => services.AddDbContext<WinAdminDbContext, SqliteWinAdminDbContext>(
                o => o.UseSqlite(database.ConnectionString)),
            DatabaseProvider.PostgreSql => services.AddDbContext<WinAdminDbContext, PostgresWinAdminDbContext>(
                o => o.UseNpgsql(database.ConnectionString)),
            _ => throw new ArgumentOutOfRangeException(nameof(database), database.Provider, "Неизвестный провайдер БД."),
        };
}
