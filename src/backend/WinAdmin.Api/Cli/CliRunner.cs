using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.CommandLine;
using WinAdmin.Core.Abstractions;
using WinAdmin.Infrastructure.Security;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Api.Cli;

public static class CliRunner
{
    public static async Task<int> RunAsync(string[] args, IConfiguration config)
    {
        // Minimal DI — only DB + user service
        var services = new ServiceCollection();
        string? configuredDbPath = config["WinAdmin:DatabasePath"];
        string dbPath = string.IsNullOrWhiteSpace(configuredDbPath)
            ? Path.Combine(AppContext.BaseDirectory, "WinAdmin.db")
            : configuredDbPath;
        services.AddDbContext<WinAdminDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));
        services.AddScoped<IUserService, UserService>();

        var provider = services.BuildServiceProvider();

        // Ensure DB is up to date
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WinAdminDbContext>();
            await db.Database.MigrateAsync();
        }

        var userCommand = new Command("user", "Управление пользователями");
        UserCommands.Register(userCommand, provider);

        var root = new RootCommand("WinAdmin CLI") { userCommand };
        return await root.InvokeAsync(args);
    }
}
