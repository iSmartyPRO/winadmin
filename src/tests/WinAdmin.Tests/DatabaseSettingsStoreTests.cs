using System.Security.Cryptography;
using Npgsql;
using WinAdmin.Core.Abstractions;
using WinAdmin.Infrastructure.Secrets;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Tests;

public sealed class DatabaseSettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winadmin-dbcfg-" + Guid.NewGuid().ToString("N"));
    private readonly AesGcmSecretProtector _protector = new(RandomNumberGenerator.GetBytes(32));
    private DatabaseSettingsStore Store(ISecretProtector? p = null) => new(_dir, p ?? _protector);

    public DatabaseSettingsStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Missing_file_falls_back_to_sqlite_path()
    {
        var s = Store().Read(@"C:\ProgramData\WinAdmin\WinAdmin.db");
        Assert.Equal(DatabaseProvider.Sqlite, s.Provider);
        Assert.Equal(@"Data Source=C:\ProgramData\WinAdmin\WinAdmin.db", s.ConnectionString);
    }

    [Theory]
    [InlineData("simple")]
    [InlineData("p;a=s's\"w")]
    public void Postgres_password_is_encrypted_on_disk_and_restored(string password)
    {
        var cs = new NpgsqlConnectionStringBuilder { Host = "db01", Database = "winadmin", Username = "wa", Password = password };
        Store().Write(new DatabaseSettings(DatabaseProvider.PostgreSql, cs.ConnectionString));

        string onDisk = File.ReadAllText(Store().FilePath);
        Assert.DoesNotContain(password, onDisk);
        Assert.Contains("enc:v1:", onDisk);
        Assert.Contains("\"provider\": \"postgresql\"", onDisk);

        var used = Store().ResolveForUse(Store().Read("unused.db"));
        Assert.Equal(password, new NpgsqlConnectionStringBuilder(used.ConnectionString).Password);
        Assert.DoesNotContain("enc:v1", Store().ResolveForUse(Store().Read("unused.db")).ConnectionString);
    }

    [Fact]
    public void Hand_written_plaintext_password_still_works()
    {
        File.WriteAllText(Store().FilePath,
            "{ \"provider\": \"postgresql\", \"connectionString\": \"Host=db;Username=wa;Password=plain\" }");
        var used = Store().ResolveForUse(Store().Read("unused.db"));
        Assert.Equal("plain", new NpgsqlConnectionStringBuilder(used.ConnectionString).Password);
    }

    [Fact]
    public void Missing_key_gives_actionable_error()
    {
        var cs = "Host=db;Username=wa;Password=secret";
        Store().Write(new DatabaseSettings(DatabaseProvider.PostgreSql, cs));

        var noKey = Store(new UnavailableSecretProtector("ключ не найден"));
        var ex = Assert.Throws<SecretUnavailableException>(() => noKey.ResolveForUse(noKey.Read("unused.db")));
        Assert.Contains("keys import", ex.Message);
    }

    [Theory]
    [InlineData("{ \"provider\": \"oracle\", \"connectionString\": \"x\" }")]
    [InlineData("{ broken")]
    [InlineData("{ \"provider\": \"sqlite\" }")]
    public void Invalid_file_reports_path(string content)
    {
        File.WriteAllText(Store().FilePath, content);
        var ex = Assert.Throws<InvalidOperationException>(() => Store().Read("unused.db"));
        Assert.Contains(Store().FilePath, ex.Message);
    }

    [Fact]
    public void Describe_hides_password()
    {
        var text = DatabaseSettingsStore.Describe(new DatabaseSettings(DatabaseProvider.PostgreSql, "Host=db;Username=wa;Password=topsecret"));
        Assert.DoesNotContain("topsecret", text);
        Assert.Contains("db", text);
    }
}
