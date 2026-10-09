using WinAdmin.Core.Abstractions;
using WinAdmin.Infrastructure.Secrets;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Tests;

public sealed class SecretProtectorFactoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winadmin-spf-" + Guid.NewGuid().ToString("N"));
    private MasterKeyStore Keys => new(Path.Combine(_dir, "keys"));

    public SecretProtectorFactoryTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Fresh_install_creates_key()
    {
        var p = SecretProtectorFactory.Open(Keys, _dir, out var problem);
        Assert.Null(problem);
        Assert.True(Keys.Exists);
        Assert.Equal("x", p.Unprotect(p.Protect("x", "t"), "t"));
    }

    [Fact]
    public void Missing_key_with_encrypted_database_settings_is_not_replaced()
    {
        File.WriteAllText(Path.Combine(_dir, DatabaseSettingsStore.FileName),
            "{ \"provider\": \"postgresql\", \"connectionString\": \"Host=db;Password=enc:v1:AAAA\" }");

        var p = SecretProtectorFactory.Open(Keys, _dir, out var problem);

        Assert.False(Keys.Exists); // keys import сможет работать без --force
        Assert.Contains("keys import", problem);
        Assert.Throws<SecretUnavailableException>(() => p.Protect("x", "t"));
    }

    [Fact]
    public void Missing_key_with_existing_jwt_file_is_not_replaced()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "keys"));
        File.WriteAllText(Path.Combine(_dir, "keys", JwtSecretStore.FileName), "enc:v1:AAAA");

        SecretProtectorFactory.Open(Keys, _dir, out var problem);

        Assert.False(Keys.Exists);
        Assert.NotNull(problem);
    }

    [Fact]
    public void Unreadable_key_reports_problem_instead_of_throwing()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "keys"));
        File.WriteAllBytes(Keys.FilePath, [1, 2, 3]);

        var p = SecretProtectorFactory.Open(Keys, _dir, out var problem);

        Assert.Contains("keys import", problem);
        Assert.Throws<SecretUnavailableException>(() => p.Unprotect("enc:v1:AAAA", "t"));
    }

    [Fact]
    public void Lazy_protector_does_not_touch_key_until_used()
    {
        int opened = 0;
        var lazy = new LazySecretProtector(() => { opened++; return SecretProtectorFactory.Open(Keys, _dir, out _); });
        Assert.Equal(0, opened);
        Assert.False(Keys.Exists);

        lazy.Protect("x", "t");
        lazy.Protect("y", "t");
        Assert.Equal(1, opened);
    }
}
