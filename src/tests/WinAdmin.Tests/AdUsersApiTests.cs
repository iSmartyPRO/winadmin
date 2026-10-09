using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.ActiveDirectory.Users;
using WinAdmin.Core.Security;

namespace WinAdmin.Tests;

[Collection("network-api")]
public sealed class AdUsersApiTests : IAsyncLifetime
{
    private const string Root = "OU=Accounts,DC=test,DC=local";
    private const string A = "OU=A," + Root;
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private readonly NetworkApiFactory _factory;
    private readonly string _sam = "u" + Guid.NewGuid().ToString("N")[..6];

    public AdUsersApiTests(NetworkApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAdStructureStore>().SaveAsync(
            AdStructureSettings.Default with { RootOu = Root, WriteMode = AdWriteMode.ProcessAccount }, null);
        await scope.ServiceProvider.GetRequiredService<IModuleRegistry>().SetEnabledAsync(AdUsersModule.ModuleId, true, "test");
        if (!_factory.AdReader.Projects.Any(p => p.Dn == A)) _factory.AdReader.Projects.Add(new AdProject(A, "A"));
        _factory.AdReader.ExistingDns.UnionWith([Root, A, "OU=Users," + A]);
        _factory.AdDomain.AddUser(_sam, "OU=Users," + A);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task List_card_and_projects()
    {
        var client = await _factory.ClientWithPermissionsAsync(PermissionIds.AdUsersRead);
        var list = await client.GetFromJsonAsync<List<JsonElement>>("/api/v1/ad/users?status=all", Web);
        Assert.Contains(list!, u => u.GetProperty("sam").GetString() == _sam);
        var card = await client.GetFromJsonAsync<JsonElement>($"/api/v1/ad/users/{_sam}", Web);
        Assert.Equal("A", card.GetProperty("user").GetProperty("projectName").GetString());
        var projects = await client.GetFromJsonAsync<List<JsonElement>>("/api/v1/ad/projects", Web);
        Assert.Contains(projects!, p => p.GetProperty("name").GetString() == "A");
    }

    [Fact]
    public async Task Write_requires_permission_and_password_is_returned_once()
    {
        var reader = await _factory.ClientWithPermissionsAsync(PermissionIds.AdUsersRead);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await reader.PostAsJsonAsync($"/api/v1/ad/users/{_sam}/password", new { generate = true, mustChange = true })).StatusCode);

        var admin = await _factory.ClientWithPermissionsAsync(PermissionIds.AdUsersPassword);
        var r = await admin.PostAsJsonAsync($"/api/v1/ad/users/{_sam}/password", new { generate = true, mustChange = true });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(20, (await r.Content.ReadFromJsonAsync<JsonElement>(Web)).GetProperty("password").GetString()!.Length);
    }

    [Fact]
    public async Task Photo_roundtrip()
    {
        var client = await _factory.ClientWithPermissionsAsync(PermissionIds.AdUsersEdit, PermissionIds.AdUsersRead);
        var content = new ByteArrayContent([0xFF, 0xD8, 0xFF, 0xE0, 1, 2]);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync($"/api/v1/ad/users/{_sam}/photo", content)).StatusCode);
        var photo = await client.GetAsync($"/api/v1/ad/users/{_sam}/photo");
        Assert.Equal("image/jpeg", photo.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Errors_map_to_status_codes()
    {
        var client = await _factory.ClientWithPermissionsAsync(PermissionIds.AdUsersRead, PermissionIds.AdUsersEdit);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/ad/users/nobody-xyz")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/v1/ad/users/{_sam}/attributes",
            new { attributes = new Dictionary<string, string> { ["userAccountControl"] = "512" } })).StatusCode);

        _factory.AdDomain.FailOn["Modify:" + _factory.AdDomain.U(_sam).Dn] = new AdWriteException(50, "Нет прав");
        try
        {
            var r = await client.PutAsJsonAsync($"/api/v1/ad/users/{_sam}/attributes", new { attributes = new Dictionary<string, string> { ["title"] = "x" } });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
            Assert.Contains("Нет прав", await r.Content.ReadAsStringAsync());
        }
        finally { _factory.AdDomain.FailOn.Clear(); }
    }

    [Fact]
    public async Task Disabled_module_is_404()
    {
        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IModuleRegistry>().SetEnabledAsync(AdUsersModule.ModuleId, false, "test");
        try
        {
            var client = await _factory.ClientWithPermissionsAsync(PermissionIds.AdUsersRead);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/ad/users")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/ad/projects")).StatusCode);
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IModuleRegistry>().SetEnabledAsync(AdUsersModule.ModuleId, true, "test");
        }
    }
}
