using System.Security.Cryptography;
using WinAdmin.Infrastructure.Secrets;

namespace WinAdmin.Tests;

public sealed class JwtSecretStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winadmin-jwt-" + Guid.NewGuid().ToString("N"));
    private readonly AesGcmSecretProtector _protector = new(RandomNumberGenerator.GetBytes(32));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Secret_survives_restart_and_is_stored_encrypted()
    {
        string first = new JwtSecretStore(_dir, _protector).GetOrCreate();
        string second = new JwtSecretStore(_dir, _protector).GetOrCreate();

        Assert.Equal(first, second);
        Assert.True(first.Length >= 32);
        string onDisk = File.ReadAllText(Path.Combine(_dir, JwtSecretStore.FileName));
        Assert.StartsWith("enc:v1:", onDisk);
        Assert.DoesNotContain(first, onDisk);
    }

    [Fact]
    public void Corrupt_file_is_replaced_instead_of_failing_start()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, JwtSecretStore.FileName), "garbage");

        string secret = new JwtSecretStore(_dir, _protector).GetOrCreate();

        Assert.False(string.IsNullOrEmpty(secret));
        Assert.Equal(secret, new JwtSecretStore(_dir, _protector).GetOrCreate());
    }

    [Fact]
    public void Configured_secret_wins_over_stored()
    {
        Assert.Equal("from-config", JwtSecretStore.Resolve("from-config", () => throw new InvalidOperationException()));
        Assert.Equal("from-store", JwtSecretStore.Resolve("  ", () => "from-store"));
    }
}
