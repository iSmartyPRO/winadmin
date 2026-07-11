using Microsoft.EntityFrameworkCore;
using WinAdmin.Core.Models;
using WinAdmin.Infrastructure.Security;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Tests;

public sealed class UserServiceTests : IDisposable
{
    private readonly WinAdminDbContext _db;
    private readonly UserService _sut;

    public UserServiceTests()
    {
        var opts = new DbContextOptionsBuilder<WinAdminDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new WinAdminDbContext(opts);
        _sut = new UserService(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task CreateAsync_StoresHashedPassword()
    {
        var req = new CreateUserRequest { Login = "alice", Password = "Secret1!", Scopes = ["admin"] };
        var dto = await _sut.CreateAsync(req);

        var entity = await _db.Users.FirstAsync(u => u.Login == "alice");
        Assert.NotEqual("Secret1!", entity.PasswordHash);
        Assert.Equal("alice", dto.Login);
        Assert.Contains("admin", dto.Scopes);
    }

    [Fact]
    public async Task ValidateAsync_CorrectPassword_ReturnsPrincipal()
    {
        await _sut.CreateAsync(new CreateUserRequest { Login = "bob", Password = "Pass1!", Scopes = ["system.read"] });
        var principal = await _sut.ValidateAsync("bob", "Pass1!");
        Assert.NotNull(principal);
        Assert.Equal("bob", principal.Login);
    }

    [Fact]
    public async Task ValidateAsync_WrongPassword_ReturnsNull()
    {
        await _sut.CreateAsync(new CreateUserRequest { Login = "carol", Password = "Pass1!", Scopes = [] });
        var principal = await _sut.ValidateAsync("carol", "wrong");
        Assert.Null(principal);
    }

    [Fact]
    public async Task ValidateAsync_InactiveUser_ReturnsNull()
    {
        var dto = await _sut.CreateAsync(new CreateUserRequest { Login = "dave", Password = "Pass1!", Scopes = [] });
        await _sut.SetActiveAsync(dto.Id, false);
        var principal = await _sut.ValidateAsync("dave", "Pass1!");
        Assert.Null(principal);
    }

    [Fact]
    public async Task ChangePasswordAsync_UpdatesHash()
    {
        var dto = await _sut.CreateAsync(new CreateUserRequest { Login = "eve", Password = "Old1!", Scopes = [] });
        await _sut.ChangePasswordAsync(dto.Id, "New1!");
        Assert.NotNull(await _sut.ValidateAsync("eve", "New1!"));
        Assert.Null(await _sut.ValidateAsync("eve", "Old1!"));
    }
}
