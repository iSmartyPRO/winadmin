using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Infrastructure.ActiveDirectory;
using WinAdmin.Infrastructure.Secrets;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Tests;

public sealed class AdStructureStoreTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private readonly ServiceProvider _sp;
    private readonly ISecretProtector _protector = new AesGcmSecretProtector(RandomNumberGenerator.GetBytes(32));

    public AdStructureStoreTests()
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

    private AdStructureStore Store(ISecretProtector? protector = null)
        => new(_sp.GetRequiredService<IServiceScopeFactory>(), protector ?? _protector);

    private string RawJson()
    {
        using var scope = _sp.CreateScope();
        return scope.ServiceProvider.GetRequiredService<WinAdminDbContext>().PlatformSettings.Single(s => s.Key == AdStructureStore.Key).Json;
    }

    private static AdStructureSettings Sample => new("OU=Accounts,DC=pcs", "Users", ["IT"], AdWriteMode.ServiceAccount, "PCS\\svc-winadmin");

    [Fact]
    public async Task Defaults_without_saved_settings()
        => Assert.Null((await Store().GetAsync()).RootOu);

    [Fact]
    public async Task Password_is_encrypted_at_rest_and_never_returned()
    {
        await Store().SaveAsync(Sample, "S3cret-Pass!");
        Assert.DoesNotContain("S3cret-Pass!", RawJson());
        Assert.Contains("enc:v1:", RawJson());

        var fresh = Store();
        var settings = await fresh.GetAsync();
        Assert.True(settings.HasWritePassword);
        Assert.Equal("OU=Accounts,DC=pcs", settings.RootOu);
        Assert.Equal("S3cret-Pass!", (await fresh.GetWriteCredentialAsync()).Password);
    }

    [Fact]
    public async Task Null_password_keeps_existing_and_empty_clears()
    {
        var store = Store();
        await store.SaveAsync(Sample, "First-Pass1!");
        await store.SaveAsync(Sample with { UsersOuName = "Staff" }, null);
        Assert.Equal("First-Pass1!", (await Store().GetWriteCredentialAsync()).Password);
        Assert.Equal("Staff", (await Store().GetAsync()).UsersOuName);

        await store.SaveAsync(Sample, "");
        Assert.False((await Store().GetAsync()).HasWritePassword);
        Assert.Null((await Store().GetWriteCredentialAsync()).Password);
    }

    [Fact]
    public async Task Process_account_needs_no_password()
    {
        await Store().SaveAsync(Sample with { WriteMode = AdWriteMode.ProcessAccount, WriteLogin = null }, null);
        var cred = await Store().GetWriteCredentialAsync();
        Assert.Equal(AdWriteMode.ProcessAccount, cred.Mode);
        Assert.Null(cred.Password);
    }

    [Fact]
    public async Task Wrong_key_reports_secret_unavailable()
    {
        await Store().SaveAsync(Sample, "S3cret-Pass!");
        var other = Store(new AesGcmSecretProtector(RandomNumberGenerator.GetBytes(32)));
        Assert.True((await other.GetAsync()).HasWritePassword);
        await Assert.ThrowsAsync<SecretUnavailableException>(() => other.GetWriteCredentialAsync());
    }

    [Fact]
    public async Task Invalid_root_is_rejected_and_nothing_saved()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Store().SaveAsync(Sample with { RootOu = "DC=pcs" }, null));
        Assert.Null((await Store().GetAsync()).RootOu);
    }
}
