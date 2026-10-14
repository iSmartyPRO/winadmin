using System.CommandLine;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using WinAdmin.Api.Cli;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.Import;
using WinAdmin.Core.Modules;
using WinAdmin.Infrastructure.Access;
using WinAdmin.Infrastructure.ActiveDirectory;
using WinAdmin.Infrastructure.Modules;
using WinAdmin.Infrastructure.Secrets;
using WinAdmin.Infrastructure.Security;
using WinAdmin.Infrastructure.Storage;
using WinAdmin.Tests.Fakes;

namespace WinAdmin.Tests;

public sealed class ImportCommandsTests : IDisposable
{
    private const string Secret = "Svc-Secret-777!";
    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private readonly ServiceProvider _sp;
    private readonly FakeAccessSource _access = new();
    private readonly FakeDirectory _directory = new();
    private readonly StringWriter _out = new();
    private readonly StringWriter _err = new();
    private readonly FakeWindowsServiceStatus _service = new();
    private readonly RootCommand _root;
    private string? _connectionUsed;

    public ImportCommandsTests()
    {
        _connection.Open();
        var catalog = new PermissionCatalog(BuiltInModules.All);
        _sp = new ServiceCollection()
            .AddDbContext<WinAdminDbContext, SqliteWinAdminDbContext>(o => o.UseSqlite(_connection))
            .AddSingleton(catalog)
            .AddSingleton(Mock.Of<IAccessService>())
            .AddScoped<IAuditService, AuditService>()
            .AddScoped<IRoleService, RoleService>()
            .AddSingleton<IDirectoryService>(_directory)
            .AddSingleton(Mock.Of<IMachineInfo>(m => m.IsDomainJoined == true))
            .AddSingleton<ISecretProtector>(new AesGcmSecretProtector(RandomNumberGenerator.GetBytes(32)))
            .AddSingleton<IModuleRegistry, ModuleRegistry>()
            .AddSingleton<IAdStructureStore, AdStructureStore>()
            .BuildServiceProvider();
        using (var scope = _sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WinAdminDbContext>();
            db.Database.Migrate();
            PlatformBootstrapper.RunAsync(db, catalog).GetAwaiter().GetResult();
        }
        _access.AppSettings = new AccessAppSettings("GESCONS", "LDAP://OU=Accounts,DC=GESCONS,DC=RU", "GESCONS", "access",
            Secret, "", "Fired Users", false);
        _access.ExcludedOus.Add("IT");
        _access.LocalUsers.Add(new("admin", "super_admin", true));
        var group = _directory.AddGroup("PortalAdmins");
        _access.RolePrincipals.Add(new("super_admin", "group", "PortalAdmins", group.DistinguishedName!, "PortalAdmins"));

        var import = new Command("import");
        ImportCommands.Register(import, _sp, cs => { _connectionUsed = cs; return _access; }, _out, _err, _service);
        _root = new RootCommand { import };
    }

    public void Dispose()
    {
        _sp.Dispose();
        _connection.Dispose();
    }

    private Task<int> Run(params string[] args) => _root.InvokeAsync(["import", "access", "--connection", "Host=pg;Database=access;Username=access", .. args]);

    [Fact]
    public async Task Dry_run_prints_report_and_changes_nothing()
    {
        Assert.Equal(0, await Run());

        string output = _out.ToString();
        Assert.Contains("Будет создано", output);
        Assert.Contains("Корневая OU", output);
        Assert.Contains("Не импортируется", output);
        Assert.Contains("Итого", output);
        Assert.Contains("--apply", output);
        Assert.DoesNotContain(Secret, output + _err);
        Assert.Null((await _sp.GetRequiredService<IAdStructureStore>().GetAsync()).RootOu);
    }

    [Fact]
    public async Task Apply_imports_and_keeps_password_out_of_output()
    {
        Assert.Equal(0, await Run("--apply", "--import-service-password", "--audit"));

        string output = _out.ToString();
        Assert.Contains("Создано", output);
        Assert.DoesNotContain(Secret, output + _err);
        Assert.Equal(Secret, (await _sp.GetRequiredService<IAdStructureStore>().GetWriteCredentialAsync()).Password);
        Assert.Equal("Host=pg;Database=access;Username=access", _connectionUsed);
    }

    [Fact]
    public async Task Unavailable_directory_fails_with_message()
    {
        _directory.Down = true;
        Assert.Equal(1, await Run("--apply"));
        Assert.Contains("ничего не изменено", _err.ToString());
        Assert.Null((await _sp.GetRequiredService<IAdStructureStore>().GetAsync()).RootOu);
    }

    [Fact]
    public async Task Failure_during_apply_prints_applied_rows_and_error_without_stack_trace()
    {
        _access.Audit.Add(new(1, DateTimeOffset.UtcNow, "ivanov", "user.move", "ad_user", "petrov", null));
        _access.AuditError = new InvalidOperationException("связь с БД Access прервана");

        Assert.Equal(1, await Run("--apply", "--audit", "--import-service-password"));

        string output = _out.ToString(), err = _err.ToString();
        Assert.Contains("Создано", output);
        Assert.Contains("Корневая OU", output);
        Assert.Contains("связь с БД Access прервана", err);
        Assert.Contains("частично", err);
        Assert.DoesNotContain("   at ", output + err);
        Assert.DoesNotContain(Secret, output + err);
    }

    [Theory]
    [InlineData(WindowsServiceState.Running)]
    [InlineData(WindowsServiceState.StartPending)]
    public async Task Apply_is_refused_while_service_runs(WindowsServiceState state)
    {
        _service.State = state;

        Assert.Equal(1, await Run("--apply"));

        Assert.Contains("Остановите службу", _err.ToString());
        Assert.Contains("--force", _err.ToString());
        Assert.Equal(["WinAdmin"], _service.Queried);
        Assert.Null((await _sp.GetRequiredService<IAdStructureStore>().GetAsync()).RootOu);
    }

    [Fact]
    public async Task Force_applies_while_service_runs()
    {
        _service.State = WindowsServiceState.Running;

        Assert.Equal(0, await Run("--apply", "--force"));

        Assert.Equal("OU=Accounts,DC=GESCONS,DC=RU", (await _sp.GetRequiredService<IAdStructureStore>().GetAsync()).RootOu);
    }

    [Theory]
    [InlineData(WindowsServiceState.Stopped)]
    [InlineData(WindowsServiceState.NotInstalled)]
    public async Task Apply_proceeds_when_service_is_stopped_or_not_installed(WindowsServiceState state)
    {
        _service.State = state;

        Assert.Equal(0, await Run("--apply"));

        Assert.Equal("OU=Accounts,DC=GESCONS,DC=RU", (await _sp.GetRequiredService<IAdStructureStore>().GetAsync()).RootOu);
    }

    [Fact]
    public async Task Dry_run_works_while_service_runs()
    {
        _service.State = WindowsServiceState.Running;
        Assert.Equal(0, await Run());
        Assert.Contains("Будет создано", _out.ToString());
    }

    [Fact]
    public void Missing_windows_service_is_reported_as_not_installed()
        => Assert.Equal(WindowsServiceState.NotInstalled,
            new WinAdmin.Infrastructure.Services.WindowsServiceStatus().GetState("WinAdmin-NoSuchService-" + Guid.NewGuid().ToString("N")[..8]));

    [Fact]
    public async Task Connection_option_is_required()
        => Assert.NotEqual(0, await _root.InvokeAsync(["import", "access"]));

    [Theory]
    [InlineData("Host=pg;Database=access;Username=u", "env-pw", "env-pw")]
    [InlineData("Host=pg;Database=access;Username=u;Password=own", "env-pw", "own")]
    [InlineData("Host=pg;Database=access;Username=u", null, null)]
    public void Password_comes_from_environment_only_when_missing(string connection, string? env, string? expected)
        => Assert.Equal(expected, new Npgsql.NpgsqlConnectionStringBuilder(ImportCommands.BuildConnectionString(connection, env)).Password);
}
