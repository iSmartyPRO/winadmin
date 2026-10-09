using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using WinAdmin.Api.Auth;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.Models;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure.Network;
using WinAdmin.Tests.Fakes;

namespace WinAdmin.Tests;

/// <summary>Общий хост для API-тестов: временный каталог данных, фейковые брандмауэр и проба портов.</summary>
public sealed class NetworkApiFactory : WebApplicationFactory<Program>
{
    public string DataDir { get; } = Path.Combine(Path.GetTempPath(), "winadmin-api-" + Guid.NewGuid().ToString("N"));
    public FakeFirewall Firewall { get; } = new();
    public HashSet<int> BusyPorts { get; } = [];
    public FakeDirectory Directory { get; } = new();
    public FakeWindowsReader Windows { get; } = new();
    public DirectorySettings DirectorySettings { get; set; } = new(true, "test.local", null, null, false);

    /// <summary>Клиент, чьи запросы сервер видит пришедшими с указанного адреса (только в тестах).</summary>
    public HttpClient ClientFrom(string ip)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        client.DefaultRequestHeaders.Add(TestRemoteIp.Header, ip);
        return client;
    }

    public HttpClient Loopback() => ClientFrom("127.0.0.1");
    public HttpClient Remote() => ClientFrom("10.1.2.3");

    public NetworkApiFactory()
    {
        System.IO.Directory.CreateDirectory(DataDir);
        // Program читает конфигурацию до Build(), поэтому — через переменные окружения.
        Environment.SetEnvironmentVariable("WinAdmin__DatabasePath", Path.Combine(DataDir, "WinAdmin.db"));
        Environment.SetEnvironmentVariable("WinAdmin__Network__VerifyDelaySeconds", "3600");
        Environment.SetEnvironmentVariable("ASPNETCORE_TEST_CONTENTROOT_WINADMIN", AppContext.BaseDirectory);
    }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IFirewallRunner>();
            services.AddSingleton<IFirewallRunner>(Firewall);
            services.RemoveAll<IPortProbe>();
            services.AddSingleton<IPortProbe>(new FakeProbe(BusyPorts));
            services.RemoveAll<IDirectoryService>();
            services.AddSingleton<IDirectoryService>(Directory);
            services.RemoveAll<IDirectorySettingsStore>();
            var settings = new Mock<IDirectorySettingsStore>();
            settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => DirectorySettings);
            settings.Setup(s => s.SaveAsync(It.IsAny<DirectorySettings>(), It.IsAny<CancellationToken>()))
                .Callback<DirectorySettings, CancellationToken>((s, _) => DirectorySettings = s.Normalize()).Returns(Task.CompletedTask);
            services.AddSingleton(settings.Object);
            services.AddSingleton<IStartupFilter, TestRemoteIp>();
            services.RemoveAll<IWindowsSignInReader>();
            services.AddSingleton<IWindowsSignInReader>(Windows);
            // Настоящий NegotiateHandler требует Kestrel (IConnectionItemsFeature) и падает в TestServer.
            services.PostConfigure<AuthenticationOptions>(o =>
                o.SchemeMap[NegotiateDefaults.AuthenticationScheme].HandlerType = typeof(ChallengeOnlyHandler));
        });
    }

    /// <summary>Клиент с API-ключом, которому назначена роль с перечисленными правами.</summary>
    public async Task<HttpClient> ClientWithPermissionsAsync(params string[] permissions)
    {
        using var scope = Services.CreateScope();
        var keys = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var roles = scope.ServiceProvider.GetRequiredService<IRoleService>();
        var created = await keys.CreateAsync(new CreateApiKeyRequest { Name = "t-" + Guid.NewGuid().ToString("N") });
        var role = await roles.CreateAsync(new SaveRoleRequest("r-" + Guid.NewGuid().ToString("N"), null,
            [.. permissions.Select(p => new RoleGrantDto(p, null))]), SystemActor());
        await roles.AssignAsync(new CreateAssignmentRequest(role.Id, PrincipalType.ApiKey, created.Key.Id, null), SystemActor());
        return WithKey(created.PlaintextKey);
    }

    public async Task<HttpClient> ClientAsAdministratorAsync()
    {
        using var scope = Services.CreateScope();
        var keys = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var roles = scope.ServiceProvider.GetRequiredService<IRoleService>();
        var created = await keys.CreateAsync(new CreateApiKeyRequest { Name = "adm-" + Guid.NewGuid().ToString("N") });
        await roles.AssignAsync(new CreateAssignmentRequest(BuiltInRoles.AdministratorId, PrincipalType.ApiKey, created.Key.Id, null), SystemActor());
        return WithKey(created.PlaintextKey);
    }

    /// <summary>Действующее лицо с полными правами (для подготовки данных в тестах).</summary>
    public IAccessContext SystemActor()
    {
        var catalog = Services.GetRequiredService<PermissionCatalog>();
        return new AccessContext(new PrincipalRef(PrincipalType.LocalUser, "test", []), "test",
            PermissionEvaluator.Evaluate([new RoleSnapshot(BuiltInRoles.AdministratorId, true, [])], catalog));
    }

    /// <summary>JWT в формате до 1b: без claim wa:principal.</summary>
    public string LegacyAccessToken(string userId)
    {
        var jwt = Services.GetRequiredService<JwtOptions>();
        var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(jwt.Secret));
        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            jwt.Issuer, jwt.Audience,
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, userId),
             new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "legacy"),
             new System.Security.Claims.Claim("scope", "admin")],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new Microsoft.IdentityModel.Tokens.SigningCredentials(key, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256));
        return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);
    }

    private HttpClient WithKey(string key)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", key);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Environment.SetEnvironmentVariable("WinAdmin__DatabasePath", null);
        Environment.SetEnvironmentVariable("WinAdmin__Network__VerifyDelaySeconds", null);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { System.IO.Directory.Delete(DataDir, recursive: true); } catch (IOException) { }
    }

    public sealed class FakeFirewall : IFirewallRunner
    {
        public List<IReadOnlyList<FirewallCommand>> Calls { get; } = [];
        public void Run(IReadOnlyList<FirewallCommand> commands) { lock (Calls) Calls.Add(commands); }
    }

    private sealed class FakeProbe(HashSet<int> busy) : IPortProbe
    {
        public bool IsInUse(int port) => busy.Contains(port);
        public bool IsListening(NetworkSettings settings) => true;
    }
}

/// <summary>Схема Negotiate в тестах: ничего не аутентифицирует, на вызов отвечает 401.</summary>
public sealed class ChallengeOnlyHandler(
    Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
    Microsoft.Extensions.Logging.ILoggerFactory logger, System.Text.Encodings.Web.UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
}

/// <summary>Результат Negotiate в тестах: Next == null — нет учётки Windows (будет вызов).</summary>
public sealed class FakeWindowsReader : IWindowsSignInReader
{
    public WindowsSignIn? Next { get; set; }
    public Task<WindowsSignIn?> ReadAsync(HttpContext context) => Task.FromResult(Next);
}

/// <summary>Тестовый адрес клиента: TestServer не заполняет RemoteIpAddress.</summary>
public sealed class TestRemoteIp : IStartupFilter
{
    public const string Header = "X-Test-Remote-Ip";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((ctx, nxt) =>
        {
            if (ctx.Request.Headers.TryGetValue(Header, out var ip) && System.Net.IPAddress.TryParse(ip, out var parsed))
                ctx.Connection.RemoteIpAddress = parsed;
            return nxt();
        });
        next(app);
    };
}

[CollectionDefinition("network-api", DisableParallelization = true)]
public sealed class NetworkApiCollection : ICollectionFixture<NetworkApiFactory>;

[Collection("network-api")]
public sealed class NetworkSettingsApiTests(NetworkApiFactory factory)
{
    private string NetworkFile => Path.Combine(factory.DataDir, "network.json");

    [Fact]
    public async Task Requires_authentication()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/settings/network");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Startup_creates_encryption_and_jwt_keys()
    {
        await factory.CreateClient().GetAsync("/health"); // гарантирует, что хост построен
        Assert.True(File.Exists(Path.Combine(factory.DataDir, "keys", "master.key")));
        Assert.True(File.Exists(Path.Combine(factory.DataDir, "keys", "jwt.key")));
    }

    [Fact]
    public async Task Requires_admin_scope()
    {
        var client = await factory.ClientWithPermissionsAsync(PermissionIds.SystemRead);
        var response = await client.GetAsync("/api/v1/settings/network");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_returns_settings_from_file_created_at_startup()
    {
        Assert.True(File.Exists(NetworkFile)); // создан при старте
        var client = await factory.ClientAsAdministratorAsync();
        var dto = await client.GetFromJsonAsync<NetworkSettingsDto>("/api/v1/settings/network", JsonOpts);
        var onDisk = new NetworkSettingsStore(factory.DataDir).ReadOrDefault(out _);

        Assert.NotNull(dto);
        Assert.Equal(onDisk.Mode, dto!.Mode);
        Assert.Equal(onDisk.Port, dto.Port);
        Assert.Equal(onDisk.Mode == NetworkMode.Network, dto.FirewallRule);
        Assert.StartsWith(onDisk.Mode == NetworkMode.Local ? "http://127.0.0.1:" : "http://localhost:", dto.Url);
    }

    [Fact]
    public async Task Invalid_settings_return_400_and_do_not_touch_file()
    {
        var client = await factory.ClientAsAdministratorAsync();
        string before = File.ReadAllText(NetworkFile);
        var response = await client.PutAsJsonAsync("/api/v1/settings/network",
            new { mode = "Network", port = 9090, allow = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, File.ReadAllText(NetworkFile));
    }

    [Fact]
    public async Task Busy_port_returns_409()
    {
        factory.BusyPorts.Add(9555);
        var client = await factory.ClientAsAdministratorAsync();
        var response = await client.PutAsJsonAsync("/api/v1/settings/network",
            new { mode = "Local", port = 9555, allow = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Valid_settings_return_url_then_write_file_and_audit()
    {
        var client = await factory.ClientAsAdministratorAsync();
        int port = 9000 + Random.Shared.Next(500);
        var response = await client.PutAsJsonAsync("/api/v1/settings/network",
            new { mode = "Network", port, allow = new[] { "10.77.77.0/24" } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<NetworkUpdateResult>(JsonOpts);
        Assert.Equal($"http://localhost:{port}", result!.Url);

        // Применение — после отправки ответа (Response.OnCompleted); аудит пишется последним.
        // Файл читаем только после аудита: чтение во время записи мешало бы приложению его записать.
        var applied = await WaitForAuditAsync($"→ network:{port} [");
        Assert.True(applied?.Success, applied?.Details);
        Assert.Contains($"\"port\": {port}", File.ReadAllText(NetworkFile));
        Assert.Contains(factory.Firewall.Calls, c => c.Any(x => x.Args.Contains("remoteip=10.77.77.0/24")));

        // Вернуть Local и дождаться применения, чтобы не влиять на другие тесты коллекции.
        await client.PutAsJsonAsync("/api/v1/settings/network", new { mode = "Local", port, allow = Array.Empty<string>() });
        var reset = await WaitForAuditAsync($"→ local:{port}");
        Assert.True(reset?.Success, reset?.Details);
        Assert.Contains("\"mode\": \"local\"", File.ReadAllText(NetworkFile));
    }

    private async Task<AuditEntryDto?> WaitForAuditAsync(string detail)
    {
        for (int i = 0; i < 150; i++)
        {
            using var scope = factory.Services.CreateScope();
            var audit = await scope.ServiceProvider.GetRequiredService<IAuditService>().QueryAsync();
            var entry = audit.FirstOrDefault(a => a.Action == "settings.network" && a.Details?.Contains(detail) == true);
            if (entry is not null) return entry;
            await Task.Delay(100);
        }
        return null;
    }

    private static readonly System.Text.Json.JsonSerializerOptions JsonOpts = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };
}
