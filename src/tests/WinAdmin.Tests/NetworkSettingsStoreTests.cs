using WinAdmin.Core.Models;
using WinAdmin.Infrastructure;
using WinAdmin.Infrastructure.Network;

namespace WinAdmin.Tests;

public sealed class NetworkSettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winadmin-net-" + Guid.NewGuid().ToString("N"));
    private readonly NetworkSettingsStore _store;

    public NetworkSettingsStoreTests() => _store = new NetworkSettingsStore(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void EnsureCreated_writes_local_with_port_from_urls()
    {
        _store.EnsureCreated(9191);
        var s = _store.ReadOrDefault(out var error);
        Assert.Null(error);
        Assert.Equal(NetworkMode.Local, s.Mode);
        Assert.Equal(9191, s.Port);
        Assert.Contains("\"mode\": \"local\"", File.ReadAllText(_store.FilePath));
    }

    [Fact]
    public void EnsureCreated_defaults_to_8080_and_does_not_overwrite_existing()
    {
        _store.EnsureCreated(null);
        Assert.Equal(8080, _store.ReadOrDefault(out _).Port);

        _store.Write(new NetworkSettings(NetworkMode.Network, 7000, ["10.0.0.0/8"]));
        _store.EnsureCreated(9999);
        var s = _store.ReadOrDefault(out _);
        Assert.Equal(NetworkMode.Network, s.Mode);
        Assert.Equal(7000, s.Port);
        Assert.Equal(new[] { "10.0.0.0/8" }, s.Allow);
    }

    [Fact]
    public void Missing_file_returns_default_without_error()
    {
        var s = _store.ReadOrDefault(out var error);
        Assert.Null(error);
        Assert.True(s.IsEquivalentTo(NetworkSettings.Default));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{ \"mode\": \"local\", \"port\": 0, \"allow\": [] }")]
    [InlineData("{ \"mode\": \"network\", \"port\": 8080, \"allow\": [] }")]
    [InlineData("{ \"mode\": \"banana\", \"port\": 8080 }")]
    public void Broken_file_returns_default_with_error_and_is_not_overwritten(string content)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_store.FilePath, content);

        var s = _store.ReadOrDefault(out var error);
        _store.EnsureCreated(9999);

        Assert.NotNull(error);
        Assert.True(s.IsEquivalentTo(NetworkSettings.Default));
        Assert.Equal(content, File.ReadAllText(_store.FilePath));
    }

    [Fact]
    public void Write_is_atomic_and_leaves_no_temp_file()
    {
        _store.Write(new NetworkSettings(NetworkMode.Local, 8081, []));
        _store.Write(new NetworkSettings(NetworkMode.Local, 8082, []));
        Assert.Equal(8082, _store.ReadOrDefault(out _).Port);
        Assert.Equal(new[] { _store.FilePath }, Directory.GetFiles(_dir));
    }

    [Fact]
    public void Paths_resolve_data_directory_from_database_path()
    {
        Assert.Equal(@"C:\ProgramData\WinAdmin", WinAdminPaths.DataDirectory(@"C:\ProgramData\WinAdmin\WinAdmin.db"));
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "WinAdmin.db"), WinAdminPaths.DatabasePath(null));
        Assert.Equal(@"D:\x.db", WinAdminPaths.DatabasePath(@"D:\x.db"));
    }
}
