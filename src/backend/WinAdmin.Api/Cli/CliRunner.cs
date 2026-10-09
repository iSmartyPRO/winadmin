using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.CommandLine;
using WinAdmin.Core.Abstractions;
using WinAdmin.Infrastructure;
using WinAdmin.Infrastructure.Network;
using WinAdmin.Infrastructure.Security;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Api.Cli;

public static class CliRunner
{
    public static async Task<int> RunAsync(string[] args, IConfiguration config)
    {
        // Minimal DI — DB + user service + network settings
        var services = new ServiceCollection();
        string dbPath = WinAdminPaths.DatabasePath(config["WinAdmin:DatabasePath"]);
        services.AddWinAdminDatabase(new DatabaseSettings(DatabaseProvider.Sqlite, $"Data Source={dbPath}"));
        services.AddScoped<IUserService, UserService>();
        services.AddWinAdminNetwork(new NetworkSettingsStore(WinAdminPaths.DataDirectory(dbPath)));

        var provider = services.BuildServiceProvider();

        // Ensure DB is up to date (only user commands need the DB)
        if (args.Length > 0 && args[0] == "user")
        {
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WinAdminDbContext>();
            await db.Database.MigrateAsync();
        }

        var userCommand = new Command("user", "Управление пользователями");
        UserCommands.Register(userCommand, provider);

        var networkCommand = new Command("network", "Сетевые настройки панели (порт, режим доступа)");
        NetworkCommands.Register(networkCommand, provider);

        var root = new RootCommand("WinAdmin CLI") { userCommand, networkCommand };
        return await root.InvokeAsync(args);
    }
}
