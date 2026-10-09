using System.CommandLine;
using System.CommandLine.Invocation;
using WinAdmin.Core.Abstractions;
using WinAdmin.Infrastructure.Secrets;

namespace WinAdmin.Api.Cli;

public static class KeysCommands
{
    private const int MinPasswordLength = 8;

    public static void Register(Command keys, MasterKeyStore store, TextWriter? output = null, TextWriter? error = null)
    {
        var o = output ?? Console.Out;
        var e = error ?? Console.Error;
        keys.AddCommand(Export(store, o, e));
        keys.AddCommand(Import(store, o, e));
    }

    private static Command Export(MasterKeyStore store, TextWriter output, TextWriter error)
    {
        var fileOpt = new Option<string>("--file", "Куда сохранить ключ") { IsRequired = true };
        var passwordOpt = new Option<string>("--password", $"Пароль для файла (не короче {MinPasswordLength} символов)") { IsRequired = true };
        var cmd = new Command("export", "Экспортировать ключ шифрования (для переноса на другой сервер)") { fileOpt, passwordOpt };
        cmd.SetHandler((InvocationContext ctx) =>
        {
            string password = ctx.ParseResult.GetValueForOption(passwordOpt)!;
            if (password.Length < MinPasswordLength)
            {
                error.WriteLine($"Пароль должен быть не короче {MinPasswordLength} символов.");
                ctx.ExitCode = 1;
                return;
            }
            if (!store.Exists)
            {
                error.WriteLine($"Ключ {store.FilePath} не найден — экспортировать нечего.");
                ctx.ExitCode = 1;
                return;
            }
            try
            {
                string file = ctx.ParseResult.GetValueForOption(fileOpt)!;
                File.WriteAllBytes(file, store.Export(password));
                output.WriteLine($"Ключ сохранён в {file}. Храните файл и пароль отдельно друг от друга.");
            }
            catch (Exception ex) when (ex is SecretUnavailableException or IOException or UnauthorizedAccessException)
            {
                error.WriteLine(ex.Message);
                ctx.ExitCode = 1;
            }
        });
        return cmd;
    }

    private static Command Import(MasterKeyStore store, TextWriter output, TextWriter error)
    {
        var fileOpt = new Option<string>("--file", "Файл экспорта ключа") { IsRequired = true };
        var passwordOpt = new Option<string>("--password", "Пароль файла") { IsRequired = true };
        var forceOpt = new Option<bool>("--force", "Заменить существующий ключ");
        var cmd = new Command("import", "Импортировать ключ шифрования") { fileOpt, passwordOpt, forceOpt };
        cmd.SetHandler((InvocationContext ctx) =>
        {
            try
            {
                byte[] data = File.ReadAllBytes(ctx.ParseResult.GetValueForOption(fileOpt)!);
                store.Import(data, ctx.ParseResult.GetValueForOption(passwordOpt)!, ctx.ParseResult.GetValueForOption(forceOpt));
                output.WriteLine($"Ключ импортирован в {store.FilePath}. Перезапустите службу WinAdmin.");
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                error.WriteLine(ex.Message);
                ctx.ExitCode = 1;
            }
        });
        return cmd;
    }
}
