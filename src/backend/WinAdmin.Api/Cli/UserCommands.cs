using System.CommandLine;
using System.CommandLine.Invocation;
using Microsoft.EntityFrameworkCore;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure.Access;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Api.Cli;

public static class UserCommands
{
    public static void Register(Command userCommand, IServiceProvider provider, TextWriter? output = null, TextWriter? error = null)
    {
        var o = output ?? Console.Out;
        var e = error ?? Console.Error;
        userCommand.AddCommand(ListCommand(provider, o));
        userCommand.AddCommand(AddCommand(provider, o, e));
        userCommand.AddCommand(PasswordCommand(provider));
        userCommand.AddCommand(DeactivateCommand(provider));
        userCommand.AddCommand(ActivateCommand(provider));
        userCommand.AddCommand(DeleteCommand(provider));
    }

    private static IUserService GetService(IServiceProvider provider)
        => provider.CreateScope().ServiceProvider.GetRequiredService<IUserService>();

    // ── list ─────────────────────────────────────────────────────
    private static Command ListCommand(IServiceProvider provider, TextWriter output)
    {
        var cmd = new Command("list", "Список пользователей");
        cmd.SetHandler(async () =>
        {
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WinAdminDbContext>();
            var users = await scope.ServiceProvider.GetRequiredService<IUserService>().ListAsync();
            if (!users.Any())
            {
                output.WriteLine("Пользователи не найдены.");
                return;
            }
            string local = nameof(PrincipalType.LocalUser);
            var roles = await db.RoleAssignments.Where(a => a.PrincipalType == local)
                .Select(a => new { a.PrincipalId, a.Role.Name }).ToListAsync();
            output.WriteLine($"{"LOGIN",-20} {"ROLES",-40} {"CREATED",-12} ACTIVE");
            output.WriteLine(new string('─', 80));
            foreach (var u in users)
            {
                string r = string.Join(", ", roles.Where(x => x.PrincipalId == u.Id).Select(x => x.Name));
                output.WriteLine($"{u.Login,-20} {(r.Length == 0 ? "—" : r),-40} {u.CreatedAt.ToString("yyyy-MM-dd"),-12} {(u.IsActive ? "yes" : "no")}");
            }
        });
        return cmd;
    }

    // ── add ──────────────────────────────────────────────────────
    private static Command AddCommand(IServiceProvider provider, TextWriter output, TextWriter error)
    {
        var loginOpt = new Option<string>("--login", "Логин") { IsRequired = true };
        var passwordOpt = new Option<string>("--password", "Пароль") { IsRequired = true };
        var roleOpt = new Option<string[]>("--role", "Роль (можно несколько): например «Администратор»") { AllowMultipleArgumentsPerToken = false };
        var scopesOpt = new Option<string>("--scopes", () => "", "Совместимость: старые scopes через запятую (admin → «Администратор»)");
        var cmd = new Command("add", "Создать пользователя") { loginOpt, passwordOpt, roleOpt, scopesOpt };
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WinAdminDbContext>();
            var catalog = scope.ServiceProvider.GetRequiredService<PermissionCatalog>();
            var roleNames = ctx.ParseResult.GetValueForOption(roleOpt) ?? [];

            var roles = new List<RoleEntity>();
            foreach (var name in roleNames)
            {
                var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == name);
                if (role is null)
                {
                    error.WriteLine($"Роль «{name}» не найдена. Список: WinAdmin.exe role list");
                    ctx.ExitCode = 1;
                    return;
                }
                roles.Add(role);
            }

            var dto = await scope.ServiceProvider.GetRequiredService<IUserService>().CreateAsync(new CreateUserRequest
            {
                Login = ctx.ParseResult.GetValueForOption(loginOpt)!,
                Password = ctx.ParseResult.GetValueForOption(passwordOpt)!,
            });
            foreach (var role in roles)
                db.RoleAssignments.Add(new RoleAssignmentEntity
                {
                    RoleId = role.Id, PrincipalType = nameof(PrincipalType.LocalUser), PrincipalId = dto.Id,
                    DisplayName = dto.Login, CreatedBy = "cli",
                });
            await LegacyScopes.AssignAsync(db, catalog, PrincipalType.LocalUser, dto.Id, dto.Login,
                ctx.ParseResult.GetValueForOption(scopesOpt)!.Split(','), "cli");
            await db.SaveChangesAsync();

            var granted = await db.RoleAssignments.Where(a => a.PrincipalId == dto.Id).Select(a => a.Role.Name).ToListAsync();
            output.WriteLine($"Создан пользователь: {dto.Login} (роли: {(granted.Count == 0 ? "нет" : string.Join(", ", granted))})");
        });
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
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WinAdminDbContext>();
            await scope.ServiceProvider.GetRequiredService<IUserService>().DeleteAsync(user.Id);
            string local = nameof(PrincipalType.LocalUser);
            db.RoleAssignments.RemoveRange(db.RoleAssignments.Where(a => a.PrincipalType == local && a.PrincipalId == user.Id));
            await db.SaveChangesAsync();
            Console.WriteLine($"Пользователь '{login}' удалён.");
        }, loginArg);
        return cmd;
    }
}
