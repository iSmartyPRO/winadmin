using System.CommandLine;
using System.Security.Cryptography;
using WinAdmin.Api.Cli;
using WinAdmin.Infrastructure.Secrets;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Tests;

public sealed class DbAndKeysCommandsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winadmin-clidb-" + Guid.NewGuid().ToString("N"));
    private readonly StringWriter _out = new();
    private readonly StringWriter _err = new();
    private readonly DatabaseSettingsStore _store;
    private readonly MasterKeyStore _keys;
    private readonly RootCommand _root;

    public DbAndKeysCommandsTests()
    {
        Directory.CreateDirectory(_dir);
        _keys = new MasterKeyStore(Path.Combine(_dir, "keys"));
        _store = new DatabaseSettingsStore(_dir, new AesGcmSecretProtector(_keys.LoadOrCreate()));
        var db = new Command("db");
        DbCommands.Register(db, _store, Path.Combine(_dir, "WinAdmin.db"), _out, _err);
        var keys = new Command("keys");
        KeysCommands.Register(keys, _keys, _out, _err);
        _root = new RootCommand { db, keys };
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task Show_prints_default_sqlite()
    {
        Assert.Equal(0, await _root.InvokeAsync(["db", "show"]));
        Assert.Contains("sqlite", _out.ToString());
        Assert.Contains("WinAdmin.db", _out.ToString());
    }

    [Fact]
    public async Task Set_sqlite_creates_and_migrates_database()
    {
        string path = Path.Combine(_dir, "other.db");
        Assert.Equal(0, await _root.InvokeAsync(["db", "set", "--provider", "sqlite", "--path", path]));
        Assert.True(File.Exists(path));
        Assert.Equal($"Data Source={path}", _store.Read("x").ConnectionString);
        Assert.Contains("Перезапустите службу", _out.ToString());
    }

    [Fact]
    public async Task Unreachable_postgres_is_not_saved()
    {
        int code = await _root.InvokeAsync(["db", "set", "--provider", "postgresql", "--connection",
            "Host=127.0.0.1;Port=1;Username=x;Password=secret;Timeout=2"]);
        Assert.Equal(1, code);
        Assert.False(_store.Exists);
        Assert.DoesNotContain("secret", _err.ToString() + _out.ToString());
    }

    [Fact]
    public async Task Keys_export_import_roundtrip_and_force_rule()
    {
        string file = Path.Combine(_dir, "key.bin");
        Assert.Equal(0, await _root.InvokeAsync(["keys", "export", "--file", file, "--password", "pw-123456"]));
        Assert.True(File.Exists(file));

        // Ключ уже есть → без --force нельзя.
        Assert.Equal(1, await _root.InvokeAsync(["keys", "import", "--file", file, "--password", "pw-123456"]));
        Assert.Contains("--force", _err.ToString());

        Assert.Equal(0, await _root.InvokeAsync(["keys", "import", "--file", file, "--password", "pw-123456", "--force"]));
    }

    [Fact]
    public async Task Short_export_password_is_rejected()
    {
        Assert.Equal(1, await _root.InvokeAsync(["keys", "export", "--file", Path.Combine(_dir, "k.bin"), "--password", "short"]));
        Assert.Contains("8", _err.ToString());
    }

    [PostgresFact]
    public async Task Set_postgres_migrates_and_encrypts_password()
    {
        var b = new Npgsql.NpgsqlConnectionStringBuilder(PostgresFactAttribute.ConnectionString)
        {
            Database = "winadmin_cli_" + Guid.NewGuid().ToString("N")[..10],
            Password = "pg-pass",
        };
        try
        {
            Assert.Equal(0, await _root.InvokeAsync(["db", "set", "--provider", "postgresql", "--connection", b.ConnectionString]));
            Assert.DoesNotContain("pg-pass", File.ReadAllText(_store.FilePath));
        }
        finally
        {
            using var ctx = DbCommands.CreateContext(_store.ResolveForUse(_store.Read("x")));
            ctx.Database.EnsureDeleted();
        }
    }
}
