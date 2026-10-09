using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.Models;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure.Access;
using WinAdmin.Infrastructure.ActiveDirectory;
using WinAdmin.Infrastructure.Security;
using WinAdmin.Infrastructure.Storage;
using WinAdmin.Tests.Fakes;

namespace WinAdmin.Tests;

public sealed class DirectorySignInTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private readonly ServiceProvider _sp;
    private readonly FakeDirectory _ad = new();
    private DirectorySettings _settings = new(true, "test.local", null, null, false);
    private readonly DirectorySignInService _signIn;
    private readonly string _helpdesk;

    public DirectorySignInTests()
    {
        _connection.Open();
        _sp = new ServiceCollection()
            .AddDbContext<WinAdminDbContext, SqliteWinAdminDbContext>(o => o.UseSqlite(_connection))
            .BuildServiceProvider();
        using (var scope = _sp.CreateScope())
            scope.ServiceProvider.GetRequiredService<WinAdminDbContext>().Database.Migrate();

        _helpdesk = _ad.AddGroup("WinAdmin-Helpdesk").Sid;
        _ad.AddUser("ivan", "Pw-1", _helpdesk);
        _ad.AddUser("petr", "Pw-2");

        var role = new RoleEntity { Name = "Helpdesk" };
        role.Permissions.Add(new RolePermissionEntity { PermissionId = PermissionIds.ServicesRead });
        using (var scope = _sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WinAdminDbContext>();
            db.Roles.Add(role);
            db.RoleAssignments.Add(new RoleAssignmentEntity { RoleId = role.Id, PrincipalType = "AdGroup", PrincipalId = _helpdesk, DisplayName = "WinAdmin-Helpdesk" });
            db.SaveChanges();
        }

        var settings = new Mock<IDirectorySettingsStore>();
        settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => _settings);
        var access = new AccessService(_sp.GetRequiredService<IServiceScopeFactory>(), new PermissionCatalog(BuiltInModules.All));
        _signIn = new DirectorySignInService(_ad, settings.Object, new AdGroupCache(_ad, settings.Object), access);
    }

    public void Dispose()
    {
        _sp.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task Group_member_signs_in()
    {
        var result = await _signIn.PasswordAsync("TEST\\ivan", "Pw-1");
        Assert.Equal(DirectorySignInStatus.Ok, result.Status);
        Assert.Equal("ivan", result.Account!.SamAccountName);
    }

    [Fact]
    public async Task User_without_assignments_has_no_access()
        => Assert.Equal(DirectorySignInStatus.NoAccess, (await _signIn.PasswordAsync("petr", "Pw-2")).Status);

    [Theory]
    [InlineData("ivan", "wrong")]
    [InlineData("nobody", "Pw-1")]
    [InlineData("ivan", "")]
    public async Task Wrong_credentials_are_rejected(string login, string password)
        => Assert.Equal(DirectorySignInStatus.InvalidCredentials, (await _signIn.PasswordAsync(login, password)).Status);

    [Fact]
    public async Task Disabled_account_cannot_sign_in()
    {
        _ad.Disable("ivan");
        Assert.Equal(DirectorySignInStatus.Disabled, (await _signIn.PasswordAsync("ivan", "Pw-1")).Status);
    }

    [Fact]
    public async Task Directory_down_is_reported()
    {
        _ad.Down = true;
        Assert.Equal(DirectorySignInStatus.Unavailable, (await _signIn.PasswordAsync("ivan", "Pw-1")).Status);
    }

    [Fact]
    public async Task Directory_switched_off_is_not_configured()
    {
        _settings = DirectorySettings.Disabled;
        Assert.Equal(DirectorySignInStatus.NotConfigured, (await _signIn.PasswordAsync("ivan", "Pw-1")).Status);
        Assert.Equal(DirectorySignInStatus.NotConfigured, (await _signIn.CompleteAsync(_ad.Users["ivan"].User.Sid)).Status);
    }

    [Fact]
    public async Task Directory_refresh_token_roundtrip_and_revoke()
    {
        using var scope = _sp.CreateScope();
        var tokens = new TokenService(scope.ServiceProvider.GetRequiredService<WinAdminDbContext>(),
            new JwtOptions { Secret = new string('k', 64), Issuer = "i", Audience = "a" });
        string raw = await tokens.CreateDirectoryRefreshTokenAsync("S-1-5-21-10-20-30-1000");
        Assert.Equal("S-1-5-21-10-20-30-1000", await tokens.ValidateDirectoryRefreshTokenAsync(raw));
        Assert.Null(await tokens.ValidateRefreshTokenAsync(raw)); // не локальный

        await tokens.RevokeRefreshTokenAsync(raw);
        Assert.Null(await tokens.ValidateDirectoryRefreshTokenAsync(raw));
    }

    [Fact]
    public void Directory_access_token_carries_sid_principal()
    {
        using var scope = _sp.CreateScope();
        var tokens = new TokenService(scope.ServiceProvider.GetRequiredService<WinAdminDbContext>(),
            new JwtOptions { Secret = new string('k', 64), Issuer = "i", Audience = "a" });
        var ivan = _ad.Users["ivan"].User;
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(tokens.GenerateDirectoryAccessToken(ivan));
        Assert.Contains(jwt.Claims, c => c.Type == PrincipalClaims.Type && c.Value == $"AdUser:{ivan.Sid}");
        Assert.DoesNotContain(jwt.Claims, c => c.Type == PrincipalClaims.GroupType);
    }
}
