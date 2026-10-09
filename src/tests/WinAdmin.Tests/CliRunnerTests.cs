using Microsoft.Extensions.Configuration;
using WinAdmin.Api.Cli;
using WinAdmin.Infrastructure.Secrets;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Tests;

/// <summary>CLI целиком (как WinAdmin.exe): аварийные сценарии с ключом и database.json.</summary>
[Collection("console")]
public sealed class CliRunnerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winadmin-cli-run-" + Guid.NewGuid().ToString("N"));
    private readonly StringWriter _err = new();
    private readonly TextWriter _originalErr = Console.Error;

    public CliRunnerTests()
    {
        Directory.CreateDirectory(_dir);
        Console.SetError(_err);
    }

    public void Dispose()
    {
        Console.SetError(_originalErr);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    private Task<int> Run(params string[] args) => CliRunner.RunAsync(args, new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["WinAdmin:DatabasePath"] = Path.Combine(_dir, "WinAdmin.db") })
        .Build());

    private string KeysDir => Path.Combine(_dir, "keys");

    private string ExportFromOtherMachine(out byte[] key)
    {
        var other = new MasterKeyStore(Path.Combine(_dir, "other-keys"));
        key = other.LoadOrCreate();
        string file = Path.Combine(_dir, "export.bin");
        File.WriteAllBytes(file, other.Export("export-pass"));
        return file;
    }

    [Fact]
    public async Task Keys_import_recovers_from_unreadable_key()
    {
        Directory.CreateDirectory(KeysDir);
        File.WriteAllBytes(Path.Combine(KeysDir, MasterKeyStore.FileName), [1, 2, 3]); // ключ с другой машины
        string file = ExportFromOtherMachine(out var key);

        int code = await Run("keys", "import", "--file", file, "--password", "export-pass", "--force");

        Assert.Equal(0, code);
        Assert.Equal(key, new MasterKeyStore(KeysDir).Load());
    }

    [Fact]
    public async Task Keys_import_without_force_works_when_key_is_missing_but_secrets_exist()
    {
        File.WriteAllText(Path.Combine(_dir, DatabaseSettingsStore.FileName),
            "{ \"provider\": \"postgresql\", \"connectionString\": \"Host=db;Password=enc:v1:AAAA\" }");
        await Run("db", "show"); // команды не должны молча создавать новый ключ
        await Run("network", "show");
        Assert.False(new MasterKeyStore(KeysDir).Exists);

        string file = ExportFromOtherMachine(out var key);
        Assert.Equal(0, await Run("keys", "import", "--file", file, "--password", "export-pass"));
        Assert.Equal(key, new MasterKeyStore(KeysDir).Load());
    }

    [Fact]
    public async Task Corrupt_database_settings_can_be_repaired_with_db_set()
    {
        File.WriteAllText(Path.Combine(_dir, DatabaseSettingsStore.FileName), "{ broken");

        Assert.Equal(1, await Run("db", "show"));
        Assert.Equal(1, await Run("user", "list"));
        Assert.DoesNotContain("   at ", _err.ToString()); // без стектрейса

        Assert.Equal(0, await Run("db", "set", "--provider", "sqlite", "--path", Path.Combine(_dir, "fixed.db")));
        Assert.Contains("fixed.db", File.ReadAllText(Path.Combine(_dir, DatabaseSettingsStore.FileName)));
    }
}

[CollectionDefinition("console", DisableParallelization = true)]
public sealed class ConsoleCollection;
