using System.CommandLine;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;

namespace WinAdmin.Api.Cli;

public static class UserCommands
{
    public static void Register(Command userCommand, IServiceProvider provider)
    {
        userCommand.AddCommand(ListCommand(provider));
        userCommand.AddCommand(AddCommand(provider));
        userCommand.AddCommand(PasswordCommand(provider));
        userCommand.AddCommand(ScopesCommand(provider));
        userCommand.AddCommand(DeactivateCommand(provider));
        userCommand.AddCommand(ActivateCommand(provider));
        userCommand.AddCommand(DeleteCommand(provider));
    }

    private static IUserService GetService(IServiceProvider provider)
        => provider.CreateScope().ServiceProvider.GetRequiredService<IUserService>();

    // ── list ─────────────────────────────────────────────────────
    private static Command ListCommand(IServiceProvider provider)
    {
        var cmd = new Command("list", "Список пользователей");
        cmd.SetHandler(async () =>
        {
            var users = await GetService(provider).ListAsync();
            if (!users.Any())
            {
                Console.WriteLine("Пользователи не найдены.");
                return;
            }
            Console.WriteLine($"{"LOGIN",-20} {"SCOPES",-40} {"CREATED",-12} ACTIVE");
            Console.WriteLine(new string('─', 80));
            foreach (var u in users)
            {
                Console.WriteLine($"{u.Login,-20} {string.Join(',', u.Scopes),-40} {u.CreatedAt.ToString("yyyy-MM-dd"),-12} {(u.IsActive ? "yes" : "no")}");
            }
        });
        return cmd;
    }

    // ── add ──────────────────────────────────────────────────────
    private static Command AddCommand(IServiceProvider provider)
    {
        var loginOpt = new Option<string>("--login", "Логин") { IsRequired = true };
        var passwordOpt = new Option<string>("--password", "Пароль") { IsRequired = true };
        var scopesOpt = new Option<string>("--scopes", () => "", "Scopes через запятую (например: admin или system.read,disks.read)");
        var cmd = new Command("add", "Создать пользователя") { loginOpt, passwordOpt, scopesOpt };
        cmd.SetHandler(async (login, password, scopesRaw) =>
        {
            var scopes = scopesRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            var dto = await GetService(provider).CreateAsync(new CreateUserRequest
            {
                Login = login,
                Password = password,
                Scopes = scopes,
            });
            Console.WriteLine($"Создан пользователь: {dto.Login} (scopes: {string.Join(',', dto.Scopes)})");
        }, loginOpt, passwordOpt, scopesOpt);
        return cmd;
    }

    // ── password ─────────────────────────────────────────────────
    private static Command PasswordCommand(IServiceProvider provider)
    {
        var loginArg = new Argument<string>("login", "Логин пользователя");
        var passwordArg = new Argument<string>("password", "Новый пароль");
        var cmd = new Command("password", "Сменить пароль") { loginArg, passwordArg };
        cmd.SetHandler(async (login, password) =>
        {
            var users = await GetService(provider).ListAsync();
            var user = users.FirstOrDefault(u => u.Login == login);
            if (user is null) { Console.Error.WriteLine($"Пользователь '{login}' не найден."); return; }
            await GetService(provider).ChangePasswordAsync(user.Id, password);
            Console.WriteLine($"Пароль пользователя '{login}' изменён.");
        }, loginArg, passwordArg);
        return cmd;
    }

    // ── scopes ───────────────────────────────────────────────────
    private static Command ScopesCommand(IServiceProvider provider)
    {
        var loginArg = new Argument<string>("login", "Логин пользователя");
        var scopesArg = new Argument<string>("scopes", "Scopes через запятую");
        var cmd = new Command("scopes", "Изменить scopes") { loginArg, scopesArg };
        cmd.SetHandler(async (login, scopesRaw) =>
        {
            var scopes = scopesRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var users = await GetService(provider).ListAsync();
            var user = users.FirstOrDefault(u => u.Login == login);
            if (user is null) { Console.Error.WriteLine($"Пользователь '{login}' не найден."); return; }
            await GetService(provider).UpdateScopesAsync(user.Id, scopes);
            Console.WriteLine($"Scopes пользователя '{login}' обновлены.");
        }, loginArg, scopesArg);
        return cmd;
    }

    // ── deactivate / activate ────────────────────────────────────
    private static Command DeactivateCommand(IServiceProvider provider) =>
        SetActiveCommand(provider, "deactivate", "Деактивировать пользователя", false);

    private static Command ActivateCommand(IServiceProvider provider) =>
        SetActiveCommand(provider, "activate", "Активировать пользователя", true);

    private static Command SetActiveCommand(IServiceProvider provider, string name, string desc, bool active)
    {
        var loginArg = new Argument<string>("login", "Логин пользователя");
        var cmd = new Command(name, desc) { loginArg };
        cmd.SetHandler(async (login) =>
        {
            var users = await GetService(provider).ListAsync();
            var user = users.FirstOrDefault(u => u.Login == login);
            if (user is null) { Console.Error.WriteLine($"Пользователь '{login}' не найден."); return; }
            await GetService(provider).SetActiveAsync(user.Id, active);
            Console.WriteLine($"Пользователь '{login}' {(active ? "активирован" : "деактивирован")}.");
        }, loginArg);
        return cmd;
    }

    // ── delete ───────────────────────────────────────────────────
    private static Command DeleteCommand(IServiceProvider provider)
    {
        var loginArg = new Argument<string>("login", "Логин пользователя");
        var cmd = new Command("delete", "Удалить пользователя") { loginArg };
        cmd.SetHandler(async (login) =>
        {
            var users = await GetService(provider).ListAsync();
            var user = users.FirstOrDefault(u => u.Login == login);
            if (user is null) { Console.Error.WriteLine($"Пользователь '{login}' не найден."); return; }
            await GetService(provider).DeleteAsync(user.Id);
            Console.WriteLine($"Пользователь '{login}' удалён.");
        }, loginArg);
        return cmd;
    }
}
