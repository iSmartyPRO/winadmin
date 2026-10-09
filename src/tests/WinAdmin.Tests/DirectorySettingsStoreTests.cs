using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Infrastructure.ActiveDirectory;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Tests;

public sealed class DirectorySettingsStoreTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private readonly ServiceProvider _sp;

    public DirectorySettingsStoreTests()
    {
        _connection.Open();
        _sp = new ServiceCollection()
            .AddDbContext<WinAdminDbContext, SqliteWinAdminDbContext>(o => o.UseSqlite(_connection))
            .BuildServiceProvider();
        using var scope = _sp.CreateScope();
        scope.ServiceProvider.GetRequiredService<WinAdminDbContext>().Database.Migrate();
    }

    public void Dispose()
    {
        _sp.Dispose();
        _connection.Dispose();
    }

    private DirectorySettingsStore Store(bool joined, string? domain)
        => new(_sp.GetRequiredService<IServiceScopeFactory>(),
            Mock.Of<IMachineInfo>(m => m.IsDomainJoined == joined && m.DomainName == domain));

    [Fact]
    public async Task Defaults_follow_the_machine()
    {
        Assert.Equal(new DirectorySettings(true, "pcs-msk.com", null, null, false), await Store(true, "pcs-msk.com").GetAsync());
        Assert.False((await Store(false, null).GetAsync()).Enabled);
    }

    [Fact]
    public async Task Saved_settings_survive_a_new_store()
    {
        var saved = new DirectorySettings(true, "pcs-msk.com", "dc.pcs-msk.com", "DC=pcs-msk,DC=com", true);
        await Store(false, null).SaveAsync(saved);
        Assert.Equal(saved, await Store(false, null).GetAsync());
    }

    [Fact]
    public async Task Enabled_without_domain_is_rejected()
        => await Assert.ThrowsAsync<ArgumentException>(() =>
            Store(false, null).SaveAsync(new DirectorySettings(true, " ", null, null, false)));

    [Fact]
    public async Task Values_are_trimmed_and_blank_becomes_null()
    {
        var store = Store(false, null);
        await store.SaveAsync(new DirectorySettings(true, " pcs-msk.com ", "  ", " DC=x ", false));
        Assert.Equal(new DirectorySettings(true, "pcs-msk.com", null, "DC=x", false), await store.GetAsync());
    }
}
