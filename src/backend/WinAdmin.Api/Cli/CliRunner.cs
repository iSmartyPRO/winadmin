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
        string dbPath = WinAdminPaths.DatabasePath(config["WinAdmin:DatabasePath"]);
        string dataDirectory = WinAdminPaths.DataDirectory(dbPath);
        var keys = new MasterKeyStore(Path.Combine(dataDirectory, "keys"));
        // Ключ открывается только когда он действительно нужен: keys import, network, db show
        // должны работать и при недоступном ключе — именно ими его восстанавливают.
        ISecretProtector protector = new LazySecretProtector(() => SecretProtectorFactory.Open(keys, dataDirectory, out _));
        var databaseStore = new DatabaseSettingsStore(dataDirectory, protector);

        // Minimal DI — DB (для user) + user service + network settings
        ServiceProvider provider;
        try
        {
            var services = new ServiceCollection();
            services.AddSingleton(protector);
            if (args is ["user", ..])
                services.AddWinAdminDatabase(databaseStore.ResolveForUse(databaseStore.Read(dbPath)));
            services.AddScoped<IUserService, UserService>();
            services.AddWinAdminNetwork(new NetworkSettingsStore(dataDirectory));
            provider = services.BuildServiceProvider();

            if (args is ["user", ..])
            {
                using var scope = provider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<WinAdminDbContext>();
                await db.Database.MigrateAsync();
            }
        }
        catch (Exception ex)
        {
            // Ошибки конфигурации (ключ, database.json, недоступная БД) — понятным текстом, без стектрейса.
            Console.Error.WriteLine(ex.GetBaseException().Message);
            return 1;
        }

        var userCommand = new Command("user", "Управление пользователями");
        UserCommands.Register(userCommand, provider);

        var networkCommand = new Command("network", "Сетевые настройки панели (порт, режим доступа)");
        NetworkCommands.Register(networkCommand, provider);

        var dbCommand = new Command("db", "База данных WinAdmin (SQLite / PostgreSQL)");
        DbCommands.Register(dbCommand, databaseStore, dbPath);

        var keysCommand = new Command("keys", "Ключ шифрования секретов");
        KeysCommands.Register(keysCommand, keys);

        var root = new RootCommand("WinAdmin CLI") { userCommand, networkCommand, dbCommand, keysCommand };
        return await root.InvokeAsync(args);
    }
}
