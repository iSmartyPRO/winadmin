using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure.Security;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Tests;

public sealed class ApiKeyServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WinAdminDbContext _db;
    private readonly ApiKeyService _service;

    public ApiKeyServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<WinAdminDbContext>().UseSqlite(_connection).Options;
        _db = new WinAdminDbContext(options);
        _db.Database.EnsureCreated();
        _service = new ApiKeyService(_db, NullLogger<ApiKeyService>.Instance);
    }

    [Fact]
    public async Task Create_then_validate_returns_principal_with_scopes()
    {
        var created = await _service.CreateAsync(new CreateApiKeyRequest
        {
            Name = "test", Scopes = [Scopes.SystemRead, Scopes.DisksRead],
        });

        Assert.StartsWith("sp_", created.PlaintextKey);

        var principal = await _service.ValidateAsync(created.PlaintextKey);
        Assert.NotNull(principal);
        Assert.Equal("test", principal!.Name);
        Assert.Contains(Scopes.SystemRead, principal.Scopes);
        Assert.Contains(Scopes.DisksRead, principal.Scopes);
    }

    [Fact]
    public async Task Validate_with_wrong_key_returns_null()
    {
        await _service.CreateAsync(new CreateApiKeyRequest { Name = "x", Scopes = [Scopes.SystemRead] });
        Assert.Null(await _service.ValidateAsync("sp_definitely_wrong"));
        Assert.Null(await _service.ValidateAsync(""));
    }

    [Fact]
    public async Task Revoked_key_no_longer_validates()
    {
        var created = await _service.CreateAsync(new CreateApiKeyRequest { Name = "x", Scopes = [Scopes.Admin] });
        Assert.NotNull(await _service.ValidateAsync(created.PlaintextKey));

        var ok = await _service.RevokeAsync(created.Key.Id);
        Assert.True(ok);
        Assert.Null(await _service.ValidateAsync(created.PlaintextKey));
    }

    [Fact]
    public async Task Expired_key_does_not_validate()
    {
        var created = await _service.CreateAsync(new CreateApiKeyRequest
        {
            Name = "x", Scopes = [Scopes.SystemRead], ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        });
        Assert.Null(await _service.ValidateAsync(created.PlaintextKey));
    }

    [Fact]
    public async Task Create_filters_unknown_scopes()
    {
        var created = await _service.CreateAsync(new CreateApiKeyRequest
        {
            Name = "x", Scopes = [Scopes.SystemRead, "not.a.real.scope"],
        });
        Assert.Equal([Scopes.SystemRead], created.Key.Scopes);
    }

    [Fact]
    public async Task Bootstrap_creates_admin_key_only_when_empty()
    {
        var raw = await _service.EnsureBootstrapAsync(presetKey: null);
        Assert.NotNull(raw);

        var principal = await _service.ValidateAsync(raw!);
        Assert.NotNull(principal);
        Assert.Contains(Scopes.Admin, principal!.Scopes);

        // Повторный вызов не создаёт второй ключ.
        var second = await _service.EnsureBootstrapAsync(presetKey: null);
        Assert.Null(second);
        Assert.Single(await _service.ListAsync());
    }

    [Fact]
    public async Task Bootstrap_honors_preset_key()
    {
        var raw = await _service.EnsureBootstrapAsync(presetKey: "sp_preset_value");
        Assert.Equal("sp_preset_value", raw);
        Assert.NotNull(await _service.ValidateAsync("sp_preset_value"));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
