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
        db.AddCommand(Show(store, defaultSqlitePath, o, e));
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

    private static Command Show(DatabaseSettingsStore store, string defaultSqlitePath, TextWriter output, TextWriter error)
    {
        var cmd = new Command("show", "Показать текущую базу данных");
        cmd.SetHandler((InvocationContext ctx) =>
        {
            output.WriteLine($"Файл:   {store.FilePath}{(store.Exists ? "" : " (нет — используется значение по умолчанию)")}");
            try
            {
                output.WriteLine($"База:   {DatabaseSettingsStore.Describe(store.Read(defaultSqlitePath))}");
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                error.WriteLine(ex.Message);
                error.WriteLine("Задайте базу заново: WinAdmin.exe db set --provider sqlite | postgresql …");
                ctx.ExitCode = 1;
            }
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

            // Испорченный database.json не должен мешать его исправлению.
            string? previous;
            try { previous = DatabaseSettingsStore.Describe(store.Read(defaultSqlitePath)); }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { previous = null; }

            try
            {
                await using var db = CreateContext(settings);
                await db.Database.MigrateAsync();
            }
            catch (Exception ex)
            {
                // Выводится только сообщение (пароль в сообщения Npgsql не попадает).
                error.WriteLine($"Не удалось подключиться к базе: {ex.GetBaseException().Message}");
                error.WriteLine("Настройки не изменены.");
                ctx.ExitCode = 1;
                return;
            }

            try
            {
                store.Write(settings);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                           or WinAdmin.Core.Abstractions.SecretUnavailableException)
            {
                error.WriteLine($"Не удалось сохранить {store.FilePath}: {ex.Message}");
                error.WriteLine("Запустите консоль от имени администратора.");
                ctx.ExitCode = 1;
                return;
            }
            output.WriteLine($"Сохранено: {DatabaseSettingsStore.Describe(settings)}");
            if (previous != DatabaseSettingsStore.Describe(settings))
                output.WriteLine("Данные из прежней базы не переносятся.");
            output.WriteLine("Перезапустите службу WinAdmin, чтобы она подключилась к новой базе.");
        });
        return cmd;
    }
}
