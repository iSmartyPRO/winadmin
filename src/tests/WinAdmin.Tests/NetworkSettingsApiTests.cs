using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Infrastructure.Network;

namespace WinAdmin.Tests;

/// <summary>Общий хост для API-тестов: временный каталог данных, фейковые брандмауэр и проба портов.</summary>
public sealed class NetworkApiFactory : WebApplicationFactory<Program>
{
    public string DataDir { get; } = Path.Combine(Path.GetTempPath(), "winadmin-api-" + Guid.NewGuid().ToString("N"));
    public FakeFirewall Firewall { get; } = new();
    public HashSet<int> BusyPorts { get; } = [];

    public NetworkApiFactory()
    {
        Directory.CreateDirectory(DataDir);
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
        });
    }

    public async Task<HttpClient> ClientWithScopesAsync(params string[] scopes)
    {
        using var scope = Services.CreateScope();
        var keys = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var created = await keys.CreateAsync(new CreateApiKeyRequest { Name = "t-" + Guid.NewGuid().ToString("N"), Scopes = [.. scopes] });
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", created.PlaintextKey);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Environment.SetEnvironmentVariable("WinAdmin__DatabasePath", null);
        Environment.SetEnvironmentVariable("WinAdmin__Network__VerifyDelaySeconds", null);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(DataDir, recursive: true); } catch (IOException) { }
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
        var client = await factory.ClientWithScopesAsync("system.read");
        var response = await client.GetAsync("/api/v1/settings/network");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_returns_settings_from_file_created_at_startup()
    {
        Assert.True(File.Exists(NetworkFile)); // создан при старте
        var client = await factory.ClientWithScopesAsync("admin");
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
        var client = await factory.ClientWithScopesAsync("admin");
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
        var client = await factory.ClientWithScopesAsync("admin");
        var response = await client.PutAsJsonAsync("/api/v1/settings/network",
            new { mode = "Local", port = 9555, allow = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Valid_settings_return_url_then_write_file_and_audit()
    {
        var client = await factory.ClientWithScopesAsync("admin");
        int port = 9000 + Random.Shared.Next(500);
        var response = await client.PutAsJsonAsync("/api/v1/settings/network",
            new { mode = "Network", port, allow = new[] { "10.77.77.0/24" } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<NetworkUpdateResult>(JsonOpts);
        Assert.Equal($"http://localhost:{port}", result!.Url);

        // Запись происходит после отправки ответа (Response.OnCompleted).
        bool written = false;
        for (int i = 0; i < 50 && !written; i++)
        {
            written = File.ReadAllText(NetworkFile).Contains($"\"port\": {port}");
            if (!written) await Task.Delay(100);
        }
        Assert.True(written);
        Assert.Contains(factory.Firewall.Calls, c => c.Any(x => x.Args.Contains("remoteip=10.77.77.0/24")));

        // Аудит пишется после файла — тоже ждём.
        bool audited = false;
        for (int i = 0; i < 50 && !audited; i++)
        {
            using var scope = factory.Services.CreateScope();
            var audit = await scope.ServiceProvider.GetRequiredService<IAuditService>().QueryAsync();
            audited = audit.Any(a => a.Action == "settings.network" && a.Success);
            if (!audited) await Task.Delay(100);
        }
        Assert.True(audited);

        // Вернуть Local и дождаться применения, чтобы не влиять на другие тесты коллекции.
        await client.PutAsJsonAsync("/api/v1/settings/network", new { mode = "Local", port, allow = Array.Empty<string>() });
        bool reset = false;
        for (int i = 0; i < 150 && !reset; i++)
        {
            reset = File.ReadAllText(NetworkFile).Contains("\"mode\": \"local\"");
            if (!reset) await Task.Delay(100);
        }
        Assert.True(reset);
    }

    private static readonly System.Text.Json.JsonSerializerOptions JsonOpts = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };
}
