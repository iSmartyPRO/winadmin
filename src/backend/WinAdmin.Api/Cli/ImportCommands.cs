using System.CommandLine;
using System.CommandLine.Invocation;
using Npgsql;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.Import;
using WinAdmin.Infrastructure.Import;

namespace WinAdmin.Api.Cli;

/// <summary>Импорт из портала Access: <c>import access --connection … [--apply] [--audit] [--overwrite] [--import-service-password]</c>.</summary>
public static class ImportCommands
{
    public const string PasswordVariable = "WINADMIN_ACCESS_PG_PASSWORD";

    public static void Register(Command import, IServiceProvider provider, Func<string, IAccessSource>? sourceFactory = null,
        TextWriter? output = null, TextWriter? error = null)
    {
        var o = output ?? Console.Out;
        var e = error ?? Console.Error;
        import.AddCommand(Access(provider, sourceFactory ?? (cs => new NpgsqlAccessSource(cs)), o, e));
    }

    /// <summary>Пароль из переменной окружения подставляется, только если в строке его нет.</summary>
    public static string BuildConnectionString(string connection, string? environmentPassword)
    {
        var builder = new NpgsqlConnectionStringBuilder(connection);
        if (string.IsNullOrEmpty(builder.Password) && !string.IsNullOrEmpty(environmentPassword))
            builder.Password = environmentPassword;
        return builder.ConnectionString;
    }

    private static Command Access(IServiceProvider provider, Func<string, IAccessSource> sourceFactory, TextWriter output, TextWriter error)
    {
        var connectionOpt = new Option<string>("--connection",
            $"Строка подключения Npgsql к БД Access (Host=…;Database=…;Username=…); пароль — в строке или в {PasswordVariable}")
            { IsRequired = true };
        var applyOpt = new Option<bool>("--apply", "Выполнить импорт (без флага — только отчёт)");
        var auditOpt = new Option<bool>("--audit", "Перенести журнал audit_log");
        var overwriteOpt = new Option<bool>("--overwrite", "Заменять уже заданные в WinAdmin значения");
        var passwordOpt = new Option<bool>("--import-service-password", "Перенести пароль служебной учётки AD (в Access — открытым текстом)");
        var cmd = new Command("access", "Импорт настроек, ролей и журнала из портала Access (PostgreSQL)")
            { connectionOpt, applyOpt, auditOpt, overwriteOpt, passwordOpt };
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var p = ctx.ParseResult;
            var options = new AccessImportOptions(p.GetValueForOption(applyOpt), p.GetValueForOption(auditOpt),
                p.GetValueForOption(overwriteOpt), p.GetValueForOption(passwordOpt));
            string connection;
            try
            {
                connection = BuildConnectionString(p.GetValueForOption(connectionOpt)!, Environment.GetEnvironmentVariable(PasswordVariable));
            }
            catch (ArgumentException ex)
            {
                error.WriteLine($"Неверная строка подключения: {ex.Message}");
                ctx.ExitCode = 1;
                return;
            }

            var source = sourceFactory(connection);
            using var scope = provider.CreateScope();
            var importer = ActivatorUtilities.CreateInstance<AccessImporter>(scope.ServiceProvider, source);
            AccessImportReport report;
            try
            {
                report = await importer.RunAsync(options, ctx.GetCancellationToken());
            }
            catch (DirectoryUnavailableException ex)
            {
                error.WriteLine($"Каталог AD недоступен ({ex.Message}) — импорт остановлен, ничего не изменено. " +
                                "Проверьте «Подключение к домену» в настройках WinAdmin.");
                ctx.ExitCode = 1;
                return;
            }
            catch (NpgsqlException ex)
            {
                error.WriteLine($"Не удалось прочитать БД Access ({source.Description}): {ex.Message}");
                ctx.ExitCode = 1;
                return;
            }
            Print(report, output);
        });
        return cmd;
    }

    public static void Print(AccessImportReport report, TextWriter output)
    {
        output.WriteLine($"Импорт из Access: {report.Source}{(report.Applied ? "" : " — только отчёт (dry run)")}");
        output.WriteLine();
        int width = report.Rows.Count == 0 ? 0 : report.Rows.Max(r => AccessImportReport.Text(r.Status).Length);
        foreach (var row in report.Rows)
        {
            output.WriteLine($"{AccessImportReport.Text(row.Status).PadRight(width)}  {row.What}");
            string pad = new(' ', width + 2);
            output.WriteLine($"{pad}откуда: {row.From}");
            output.WriteLine($"{pad}куда:   {row.To}");
            if (!string.IsNullOrEmpty(row.Note))
                output.WriteLine($"{pad}{row.Note}");
        }
        output.WriteLine();
        output.WriteLine($"Итого: {(report.Rows.Count == 0 ? "нечего импортировать" : report.Summary())}");
        foreach (var hint in report.Hints)
            output.WriteLine($"• {hint}");
    }
}
