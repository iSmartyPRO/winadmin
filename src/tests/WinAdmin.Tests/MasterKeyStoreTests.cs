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
    public void Concurrent_creation_yields_one_key()
    {
        // Служба и CLI стартуют одновременно на чистой установке — ключ должен быть один.
        string keysDir = Path.Combine(_dir, "race");
        var keys = new byte[8][];
        using var start = new Barrier(keys.Length);
        Parallel.For(0, keys.Length, new ParallelOptions { MaxDegreeOfParallelism = keys.Length }, i =>
        {
            start.SignalAndWait();
            keys[i] = new MasterKeyStore(keysDir).LoadOrCreate();
        });

        byte[] onDisk = new MasterKeyStore(keysDir).Load();
        Assert.All(keys, k => Assert.Equal(onDisk, k));
    }

    [Fact]
    public void Keys_directory_is_protected_and_users_are_not_granted()
    {
        var store = new MasterKeyStore(Path.Combine(_dir, "acl"));
        store.LoadOrCreate();

        var dirAcl = new DirectoryInfo(store.KeysDirectory).GetAccessControl();
        Assert.True(dirAcl.AreAccessRulesProtected);
        var sids = dirAcl.GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier))
            .Cast<FileSystemAccessRule>().Select(r => r.IdentityReference.Value).ToList();
        Assert.DoesNotContain("S-1-5-32-545", sids); // Пользователи
        Assert.DoesNotContain("S-1-5-11", sids);     // Прошедшие проверку
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
