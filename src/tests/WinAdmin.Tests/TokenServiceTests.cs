using System.IdentityModel.Tokens.Jwt;
using Microsoft.EntityFrameworkCore;
using WinAdmin.Core.Models;
using WinAdmin.Infrastructure.Security;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Tests;

public sealed class TokenServiceTests : IDisposable
{
    private readonly WinAdminDbContext _db;
    private readonly TokenService _sut;
    private readonly JwtOptions _opts = new()
    {
        Secret = "test-secret-key-must-be-at-least-32-chars!!",
        AccessTokenMinutes = 60,
        RefreshTokenDays = 30,
        Issuer = "WinAdmin",
        Audience = "WinAdmin",
    };

    public TokenServiceTests()
    {
        var dbOpts = new DbContextOptionsBuilder<WinAdminDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new WinAdminDbContext(dbOpts);
        _sut = new TokenService(_db, _opts);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void GenerateAccessToken_ContainsLoginAndScopes()
    {
        var principal = new UserPrincipal { Id = "1", Login = "admin", Scopes = ["admin"] };
        var token = _sut.GenerateAccessToken(principal);
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);
        Assert.Equal("admin", jwt.Claims.First(c => c.Type == System.Security.Claims.ClaimTypes.Name).Value);
        Assert.Contains(jwt.Claims, c => c.Type == "scope" && c.Value == "admin");
    }

    [Fact]
    public async Task CreateAndValidateRefreshToken_ReturnsPrincipal()
    {
        var user = new UserEntity { Login = "alice", PasswordHash = "x", Scopes = "admin" };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var raw = await _sut.CreateRefreshTokenAsync(user.Id);
        var principal = await _sut.ValidateRefreshTokenAsync(raw);

        Assert.NotNull(principal);
        Assert.Equal("alice", principal.Login);
    }

    [Fact]
    public async Task RevokeRefreshToken_ValidateReturnsNull()
    {
        var user = new UserEntity { Login = "bob", PasswordHash = "x", Scopes = "admin" };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var raw = await _sut.CreateRefreshTokenAsync(user.Id);
        await _sut.RevokeRefreshTokenAsync(raw);
        var principal = await _sut.ValidateRefreshTokenAsync(raw);

        Assert.Null(principal);
    }
}
