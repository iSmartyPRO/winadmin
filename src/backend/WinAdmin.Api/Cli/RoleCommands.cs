using System.CommandLine;
using System.CommandLine.Invocation;
using Microsoft.EntityFrameworkCore;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Api.Cli;

/// <summary>Роли из консоли администратора — в том числе аварийная выдача «Администратор».</summary>
public static class RoleCommands
{
    public static void Register(Command role, IServiceProvider provider, TextWriter? output = null, TextWriter? error = null)
    {
        var o = output ?? Console.Out;
        var e = error ?? Console.Error;
        role.AddCommand(List(provider, o));
        role.AddCommand(Change("assign", "Назначить роль", provider, o, e, assign: true));
        role.AddCommand(Change("unassign", "Снять роль", provider, o, e, assign: false));
    }

    private static Command List(IServiceProvider provider, TextWriter output)
    {
        var cmd = new Command("list", "Роли и кому они назначены");
        cmd.SetHandler(async () =>
        {
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WinAdminDbContext>();
            foreach (var r in await db.Roles.Include(x => x.Assignments).OrderByDescending(x => x.IsBuiltin).ThenBy(x => x.Name).ToListAsync())
            {
                output.WriteLine($"{r.Name}{(r.IsBuiltin ? " (встроенная)" : "")}");
                foreach (var a in r.Assignments.OrderBy(x => x.DisplayName))
                    output.WriteLine($"    {a.PrincipalType,-10} {a.DisplayName}");
            }
        });
        return cmd;
    }

    private static Command Change(string name, string description, IServiceProvider provider, TextWriter output, TextWriter error, bool assign)
    {
        var roleOpt = new Option<string>("--role", "Название роли") { IsRequired = true };
        var localOpt = new Option<string?>("--local", "Логин локального пользователя");
        var keyOpt = new Option<string?>("--apikey", "Id API-ключа");
        var cmd = new Command(name, description) { roleOpt, localOpt, keyOpt };
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WinAdminDbContext>();
            string roleName = ctx.ParseResult.GetValueForOption(roleOpt)!;
            var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == roleName);
            if (role is null)
            {
                error.WriteLine($"Роль «{roleName}» не найдена.");
                ctx.ExitCode = 1;
                return;
            }

            string? login = ctx.ParseResult.GetValueForOption(localOpt);
            string? keyId = ctx.ParseResult.GetValueForOption(keyOpt);
            (PrincipalType Type, string Id, string Display)? principal = null;
            if (login is not null && await db.Users.FirstOrDefaultAsync(u => u.Login == login) is { } user)
                principal = (PrincipalType.LocalUser, user.Id, user.Login);
            else if (keyId is not null && await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == keyId) is { } key)
                principal = (PrincipalType.ApiKey, key.Id, key.Name);
            if (principal is null)
            {
                error.WriteLine("Укажите существующего пользователя (--local) или API-ключ (--apikey).");
                ctx.ExitCode = 1;
                return;
            }

            string type = principal.Value.Type.ToString();
            var existing = await db.RoleAssignments.FirstOrDefaultAsync(a =>
                a.RoleId == role.Id && a.PrincipalType == type && a.PrincipalId == principal.Value.Id);
            if (assign && existing is null)
                db.RoleAssignments.Add(new RoleAssignmentEntity
                {
                    RoleId = role.Id, PrincipalType = type, PrincipalId = principal.Value.Id,
                    DisplayName = principal.Value.Display, CreatedBy = "cli",
                });
            else if (!assign && existing is not null)
                db.RoleAssignments.Remove(existing);
            await db.SaveChangesAsync();
            output.WriteLine($"{(assign ? "Назначено" : "Снято")}: «{role.Name}» — {principal.Value.Display}. Служба применит изменение в течение минуты.");
        });
        return cmd;
    }
}
