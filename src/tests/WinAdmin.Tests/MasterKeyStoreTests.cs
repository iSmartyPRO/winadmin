using System.Security.AccessControl;
using WinAdmin.Core.Abstractions;
using WinAdmin.Infrastructure.Secrets;

namespace WinAdmin.Tests;

public sealed class MasterKeyStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winadmin-keys-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Creates_once_and_reloads_same_key()
    {
        var store = new MasterKeyStore(Path.Combine(_dir, "a"));
        byte[] first = store.LoadOrCreate();
        byte[] again = new MasterKeyStore(Path.Combine(_dir, "a")).LoadOrCreate();

        Assert.Equal(32, first.Length);
        Assert.Equal(first, again);
        Assert.NotEqual(first, File.ReadAllBytes(store.FilePath)); // на диске — под DPAPI
        Assert.True(new FileInfo(store.FilePath).GetAccessControl().AreAccessRulesProtected);
    }

    [Fact]
    public void Export_and_import_move_key_to_another_folder()
    {
        var source = new MasterKeyStore(Path.Combine(_dir, "src"));
        byte[] key = source.LoadOrCreate();
        byte[] exported = source.Export("correct horse battery");

        var target = new MasterKeyStore(Path.Combine(_dir, "dst"));
        target.Import(exported, "correct horse battery", overwrite: false);

        Assert.Equal(key, target.Load());
    }

    [Fact]
    public void Wrong_password_is_rejected()
    {
        var source = new MasterKeyStore(Path.Combine(_dir, "src"));
        source.LoadOrCreate();
        byte[] exported = source.Export("right");

        var target = new MasterKeyStore(Path.Combine(_dir, "dst"));
        var ex = Assert.Throws<InvalidOperationException>(() => target.Import(exported, "wrong", overwrite: false));
        Assert.Contains("пароль", ex.Message);
        Assert.False(target.Exists);
    }

    [Fact]
    public void Import_over_existing_key_requires_overwrite()
    {
        var source = new MasterKeyStore(Path.Combine(_dir, "src"));
        source.LoadOrCreate();
        byte[] exported = source.Export("pw");

        var target = new MasterKeyStore(Path.Combine(_dir, "dst"));
        byte[] own = target.LoadOrCreate();

        Assert.Throws<InvalidOperationException>(() => target.Import(exported, "pw", overwrite: false));
        Assert.Equal(own, target.Load());

        target.Import(exported, "pw", overwrite: true);
        Assert.Equal(source.Load(), target.Load());
    }

    [Fact]
    public void Corrupt_key_file_reports_secret_unavailable()
    {
        var store = new MasterKeyStore(Path.Combine(_dir, "bad"));
        Directory.CreateDirectory(store.KeysDirectory);
        File.WriteAllBytes(store.FilePath, [1, 2, 3, 4]);

        var ex = Assert.Throws<SecretUnavailableException>(() => store.Load());
        Assert.Contains("keys import", ex.Message);
    }
}
