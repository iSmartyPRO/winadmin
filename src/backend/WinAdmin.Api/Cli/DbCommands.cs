using System.CommandLine;
using System.CommandLine.Invocation;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Api.Cli;

public static class DbCommands
{
    public static void Register(Command db, DatabaseSettingsStore store, string defaultSqlitePath,
        TextWriter? output = null, TextWriter? error = null)
    {
        var o = output ?? Console.Out;
        var e = error ?? Console.Error;
        db.AddCommand(Show(store, defaultSqlitePath, o));
        db.AddCommand(Set(store, defaultSqlitePath, o, e));
    }

    /// <summary>Контекст выбранного провайдера без DI (для CLI).</summary>
    public static WinAdminDbContext CreateContext(DatabaseSettings resolved) => resolved.Provider switch
    {
        DatabaseProvider.PostgreSql => new PostgresWinAdminDbContext(
            new DbContextOptionsBuilder<PostgresWinAdminDbContext>().UseNpgsql(resolved.ConnectionString).Options),
        _ => new SqliteWinAdminDbContext(
            new DbContextOptionsBuilder<SqliteWinAdminDbContext>().UseSqlite(resolved.ConnectionString).Options),
    };

    private static Command Show(DatabaseSettingsStore store, string defaultSqlitePath, TextWriter output)
    {
        var cmd = new Command("show", "Показать текущую базу данных");
        cmd.SetHandler(() =>
        {
            output.WriteLine($"Файл:   {store.FilePath}{(store.Exists ? "" : " (нет — используется значение по умолчанию)")}");
            output.WriteLine($"База:   {DatabaseSettingsStore.Describe(store.Read(defaultSqlitePath))}");
        });
        return cmd;
    }

    private static Command Set(DatabaseSettingsStore store, string defaultSqlitePath, TextWriter output, TextWriter error)
    {
        var providerOpt = new Option<string>("--provider", "sqlite или postgresql") { IsRequired = true };
        var pathOpt = new Option<string?>("--path", "Путь к файлу SQLite (для sqlite)");
        var connectionOpt = new Option<string?>("--connection", "Строка подключения PostgreSQL: Host=…;Database=…;Username=…;Password=…");
        var cmd = new Command("set", "Выбрать базу данных (проверяет подключение и применяет миграции)")
            { providerOpt, pathOpt, connectionOpt };

        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            DatabaseProvider provider;
            try
            {
                provider = DatabaseSettingsStore.ParseProvider(ctx.ParseResult.GetValueForOption(providerOpt)!);
            }
            catch (ArgumentException ex)
            {
                error.WriteLine(ex.Message);
                ctx.ExitCode = 1;
                return;
            }

            DatabaseSettings settings;
            if (provider == DatabaseProvider.Sqlite)
            {
                string path = ctx.ParseResult.GetValueForOption(pathOpt) ?? defaultSqlitePath;
                settings = new DatabaseSettings(provider, $"Data Source={Path.GetFullPath(path)}");
            }
            else
            {
                string? connection = ctx.ParseResult.GetValueForOption(connectionOpt);
                if (string.IsNullOrWhiteSpace(connection))
                {
                    error.WriteLine("Для postgresql укажите --connection.");
                    ctx.ExitCode = 1;
                    return;
                }
                settings = new DatabaseSettings(provider, connection);
            }

            var previous = store.Read(defaultSqlitePath);
            try
            {
                await using var db = CreateContext(settings);
                await db.Database.MigrateAsync();
            }
            catch (Exception ex) when (ex is NpgsqlException or Microsoft.Data.Sqlite.SqliteException
                                           or InvalidOperationException or ArgumentException or TimeoutException)
            {
                error.WriteLine($"Не удалось подключиться к базе: {ex.GetBaseException().Message}");
                error.WriteLine("Настройки не изменены.");
                ctx.ExitCode = 1;
                return;
            }

            store.Write(settings);
            output.WriteLine($"Сохранено: {DatabaseSettingsStore.Describe(settings)}");
            if (DatabaseSettingsStore.Describe(previous) != DatabaseSettingsStore.Describe(settings))
                output.WriteLine("Данные из прежней базы не переносятся.");
            output.WriteLine("Перезапустите службу WinAdmin, чтобы она подключилась к новой базе.");
        });
        return cmd;
    }
}
