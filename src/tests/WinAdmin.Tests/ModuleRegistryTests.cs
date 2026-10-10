using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Modules;
using WinAdmin.Infrastructure.Modules;
using WinAdmin.Infrastructure.Secrets;
using WinAdmin.Infrastructure.Storage;

namespace WinAdmin.Tests;

public sealed class DirectoryTestSettings
{
    [Description("Служебная учётка")]
    public string ServiceUser { get; set; } = "";

    [Secret, Description("Пароль служебной учётки")]
    public string ServicePassword { get; set; } = "";

    public int Port { get; set; } = 389;
    public bool UseLdaps { get; set; }
    public List<string> HiddenOus { get; set; } = [];
}

internal sealed class ServerOnlyModule(ModuleRequirements requirements, bool enabledByDefault = true) : IWinAdminModule
{
    public string Id => "dir";
    public string Title => "Каталог";
    public string? Description => null;
    public ModuleRequirements Requirements => requirements;
    public bool EnabledByDefault => enabledByDefault;
    public IReadOnlyList<PermissionDefinition> Permissions { get; } = [new("dir.read", "Чтение")];
    public Type? SettingsType => typeof(DirectoryTestSettings);
    public IScopeProvider? Scope => null;
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration) { }
}

public sealed class ModuleRegistryTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private readonly ServiceProvider _sp;
    private readonly Mock<IMachineInfo> _machine = new();
    private readonly Mock<IAccessService> _access = new();
    private readonly AesGcmSecretProtector _protector = new(RandomNumberGenerator.GetBytes(32));

    public ModuleRegistryTests()
    {
        _connection.Open();
        _sp = new ServiceCollection()
            .AddDbContext<WinAdminDbContext, SqliteWinAdminDbContext>(o => o.UseSqlite(_connection))
            .AddSingleton(Mock.Of<IAuditService>())
            .BuildServiceProvider();
        using var scope = _sp.CreateScope();
        scope.ServiceProvider.GetRequiredService<WinAdminDbContext>().Database.Migrate();
    }

    public void Dispose()
    {
        _sp.Dispose();
        _connection.Dispose();
    }

    private ModuleRegistry Registry(IWinAdminModule module) => new(
        new PermissionCatalog([module]), _machine.Object, _sp.GetRequiredService<IServiceScopeFactory>(), _protector, _access.Object);

    private string? StoredJson()
    {
        using var scope = _sp.CreateScope();
        return scope.ServiceProvider.GetRequiredService<WinAdminDbContext>().ModuleStates.Single().SettingsJson;
    }

    [Fact]
    public async Task Toggle_persists_and_survives_a_new_registry_instance()
    {
        var module = new ServerOnlyModule(ModuleRequirements.None);
        Assert.True(Registry(module).GetState("dir").Enabled);

        await Registry(module).SetEnabledAsync("dir", false, "admin");

        Assert.False(Registry(module).GetState("dir").Enabled);
        _access.Verify(a => a.Invalidate(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Unmet_requirements_make_module_unavailable_and_not_enableable()
    {
        _machine.SetupGet(m => m.IsWindowsServer).Returns(false);
        var registry = Registry(new ServerOnlyModule(ModuleRequirements.WindowsServer));

        var state = registry.GetState("dir");
        Assert.False(state.Available);
        Assert.False(state.Enabled);
        Assert.Contains("Windows Server", state.UnavailableReason);
        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.SetEnabledAsync("dir", true, "admin"));
    }

    [Fact]
    public void Schema_describes_fields_and_secrets()
    {
        var schema = ModuleSettingsSchema.Build(typeof(DirectoryTestSettings));
        Assert.Equal(
            new[] { "serviceUser:string", "servicePassword:secret", "port:number", "useLdaps:boolean", "hiddenOus:stringList" },
            schema.Select(f => $"{f.Name}:{f.Kind}"));
        Assert.Equal("Пароль служебной учётки", schema[1].Title);
    }

    [Fact]
    public async Task Secret_is_encrypted_masked_and_kept_when_not_resent()
    {
        var registry = Registry(new ServerOnlyModule(ModuleRequirements.None));
        await registry.SaveSettingsAsync("dir", new JsonObject
        {
            ["serviceUser"] = "svc_access", ["servicePassword"] = "P@ss;1", ["port"] = 636,
        }, "admin");

        Assert.DoesNotContain("P@ss;1", StoredJson());
        Assert.Contains("enc:v1:", StoredJson());

        var view = await registry.GetSettingsViewAsync("dir");
        Assert.Equal("svc_access", view["serviceUser"]!.GetValue<string>());
        Assert.True(view["servicePassword"]!["isSet"]!.GetValue<bool>());

        // Пароль не прислан — остаётся прежним.
        await registry.SaveSettingsAsync("dir", new JsonObject { ["serviceUser"] = "svc2", ["servicePassword"] = "" }, "admin");
        var typed = await registry.GetSettingsAsync<DirectoryTestSettings>("dir");
        Assert.Equal("svc2", typed.ServiceUser);
        Assert.Equal("P@ss;1", typed.ServicePassword);
        Assert.Equal(636, typed.Port);
    }

    [Fact]
    public async Task View_shows_defaults_for_fields_never_saved()
    {
        var registry = Registry(new ServerOnlyModule(ModuleRequirements.None));
        var view = await registry.GetSettingsViewAsync("dir");
        Assert.Equal(389, view["port"]!.GetValue<int>());
        Assert.Empty(view["hiddenOus"]!.AsArray());
        Assert.False(view["servicePassword"]!["isSet"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Empty_number_resets_to_default_instead_of_failing(string? empty)
    {
        var registry = Registry(new ServerOnlyModule(ModuleRequirements.None));
        await registry.SaveSettingsAsync("dir", new JsonObject { ["port"] = 636 }, "admin");
        await registry.SaveSettingsAsync("dir", new JsonObject { ["port"] = empty, ["serviceUser"] = "svc" }, "admin");

        var typed = await registry.GetSettingsAsync<DirectoryTestSettings>("dir");
        Assert.Equal(389, typed.Port);
        Assert.Equal("svc", typed.ServiceUser);
        Assert.Equal(389, (await registry.GetSettingsViewAsync("dir"))["port"]!.GetValue<int>());
    }

    [Fact]
    public async Task Wrong_types_are_rejected()
    {
        var registry = Registry(new ServerOnlyModule(ModuleRequirements.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            registry.SaveSettingsAsync("dir", new JsonObject { ["port"] = "not a number" }, "admin"));
    }
}
