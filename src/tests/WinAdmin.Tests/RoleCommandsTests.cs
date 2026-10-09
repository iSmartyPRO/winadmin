using System.CommandLine;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Api.Cli;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure.Access;
using WinAdmin.Infrastructure.Security;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Tests;

public sealed class RoleCommandsTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private readonly ServiceProvider _sp;
    private readonly StringWriter _out = new();
    private readonly StringWriter _err = new();
    private readonly RootCommand _root;

    public RoleCommandsTests()
    {
        _connection.Open();
        _sp = new ServiceCollection()
            .AddDbContext<WinAdminDbContext, SqliteWinAdminDbContext>(o => o.UseSqlite(_connection))
            .AddSingleton(new PermissionCatalog(BuiltInModules.All))
            .AddScoped<IUserService, UserService>()
            .BuildServiceProvider();
        using (var scope = _sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WinAdminDbContext>();
            db.Database.Migrate();
            PlatformBootstrapper.RunAsync(db, scope.ServiceProvider.GetRequiredService<PermissionCatalog>()).GetAwaiter().GetResult();
        }
        var user = new Command("user");
        UserCommands.Register(user, _sp, _out, _err);
        var role = new Command("role");
        RoleCommands.Register(role, _sp, _out, _err);
        _root = new RootCommand { user, role };
    }

    public void Dispose()
    {
        _sp.Dispose();
        _connection.Dispose();
    }

    private T Db<T>(Func<WinAdminDbContext, T> query)
    {
        using var scope = _sp.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<WinAdminDbContext>());
    }

    [Fact]
    public async Task User_add_with_role_assigns_administrator()
    {
        Assert.Equal(0, await _root.InvokeAsync(["user", "add", "--login", "ops", "--password", "Ops-123456", "--role", "Администратор"]));
        Assert.Equal(1, Db(db => db.RoleAssignments.Count(a => a.RoleId == BuiltInRoles.AdministratorId && a.DisplayName == "ops")));
    }

    [Fact]
    public async Task User_add_with_legacy_scopes_maps_to_roles()
    {
        Assert.Equal(0, await _root.InvokeAsync(["user", "add", "--login", "mon", "--password", "Mon-1234567", "--scopes", "system.read,services.read"]));
        Assert.Equal(1, Db(db => db.RoleAssignments.Count(a => a.DisplayName == "mon")));
        Assert.Equal("", Db(db => db.Users.Single(u => u.Login == "mon").Scopes));
    }

    [Fact]
    public async Task Unknown_role_fails_without_creating_user()
    {
        Assert.Equal(1, await _root.InvokeAsync(["user", "add", "--login", "x", "--password", "X-12345678", "--role", "Нет такой"]));
        Assert.Equal(0, Db(db => db.Users.Count()));
        Assert.Contains("Нет такой", _err.ToString());
    }

    [Fact]
    public async Task User_delete_removes_role_assignments()
    {
        await _root.InvokeAsync(["user", "add", "--login", "tmp", "--password", "Tmp-1234567", "--role", "Администратор"]);
        await _root.InvokeAsync(["user", "add", "--login", "keep", "--password", "Keep-123456", "--role", "Администратор"]);
        Assert.Equal(0, await _root.InvokeAsync(["user", "delete", "tmp"]));
        Assert.Equal(0, Db(db => db.RoleAssignments.Count(a => a.DisplayName == "tmp")));
    }

    [Fact]
    public async Task Role_assign_is_the_emergency_path()
    {
        await _root.InvokeAsync(["user", "add", "--login", "late", "--password", "Late-123456"]);
        Assert.Equal(0, await _root.InvokeAsync(["role", "assign", "--role", "Администратор", "--local", "late"]));
        Assert.Equal(0, await _root.InvokeAsync(["role", "list"]));
        Assert.Contains("Администратор", _out.ToString());
        Assert.Contains("late", _out.ToString());
    }
}
