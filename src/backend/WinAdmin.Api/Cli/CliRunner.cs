using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.CommandLine;
using WinAdmin.Core.Abstractions;
using WinAdmin.Infrastructure;
using WinAdmin.Infrastructure.Network;
using WinAdmin.Infrastructure.Secrets;
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
        string dataDirectory = WinAdminPaths.DataDirectory(dbPath);
        var keys = new MasterKeyStore(Path.Combine(dataDirectory, "keys"));
        ISecretProtector protector = keys.Exists || args is ["user", ..]
            ? new AesGcmSecretProtector(keys.LoadOrCreate())
            : new UnavailableSecretProtector($"Ключ шифрования {keys.FilePath} не найден.");
        var databaseStore = new DatabaseSettingsStore(dataDirectory, protector);

        services.AddSingleton(protector);
        if (args is ["user", ..])
            services.AddWinAdminDatabase(databaseStore.ResolveForUse(databaseStore.Read(dbPath)));
        services.AddScoped<IUserService, UserService>();
        services.AddWinAdminNetwork(new NetworkSettingsStore(dataDirectory));

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
