using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.ActiveDirectory.Folders;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure.ActiveDirectory.Folders;

namespace WinAdmin.Tests;

[Collection("network-api")]
public sealed class AdFoldersApiTests : IAsyncLifetime
{
    private const string Root = "OU=Accounts,DC=test,DC=local";
    private const string A = "OU=A," + Root;
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private readonly NetworkApiFactory _factory;
    private readonly string _base = "f" + Guid.NewGuid().ToString("N")[..6];

    public AdFoldersApiTests(NetworkApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAdStructureStore>().SaveAsync(
            AdStructureSettings.Default with { RootOu = Root, WriteMode = AdWriteMode.ProcessAccount }, null);
        var registry = scope.ServiceProvider.GetRequiredService<IModuleRegistry>();
        await registry.SetEnabledAsync(AdFoldersModule.ModuleId, true, "test");
        await registry.SaveSettingsAsync(AdFoldersModule.ModuleId, new System.Text.Json.Nodes.JsonObject
        {
            ["driveMappings"] = new System.Text.Json.Nodes.JsonArray(@"A=\\fs01\Projects"),
        }, "test");
        if (!_factory.AdReader.Projects.Any(p => p.Dn == A)) _factory.AdReader.Projects.Add(new AdProject(A, "A"));
        _factory.AdReader.ExistingDns.UnionWith([Root, A]);
        _factory.AdDomain.AddGroup($"sg_a_{_base}_full", A).Description = $@"A:\A\{_base};Full Access";
        _factory.AdDomain.AddGroup($"sg_a_{_base}_read", A).Description = $@"A:\A\{_base};Read Only";
        _factory.AdDomain.AddUser("u" + _base, "OU=Users," + A);
        _factory.Services.GetRequiredService<FolderCatalogCache>().Invalidate();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task List_membership_and_user_access()
    {
        var client = await _factory.ClientWithPermissionsAsync(PermissionIds.AdFoldersRead, PermissionIds.AdFoldersMembership);
        var catalog = await client.GetFromJsonAsync<JsonElement>($"/api/v1/ad/folders?q={_base}", Web);
        var folder = catalog.GetProperty("folders").EnumerateArray().Single();
        string fullDn = folder.GetProperty("full").GetProperty("dn").GetString()!;

        var add = await client.PostAsJsonAsync("/api/v1/ad/folders/membership", new { groupDn = fullDn, member = "u" + _base, add = true, removeFromOther = true });
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        var access = await client.GetFromJsonAsync<List<JsonElement>>($"/api/v1/ad/folders/user/u{_base}", Web);
        Assert.True(Assert.Single(access!).GetProperty("hasFull").GetBoolean());
    }

    [Fact]
    public async Task Preview_create_and_acl()
    {
        var client = await _factory.ClientWithPermissionsAsync(PermissionIds.AdFoldersRead, PermissionIds.AdFoldersCreate);
        var request = new { projectDn = A, path = $@"A:\A\new{_base}", baseName = "n" + _base, orgCode = "a", createDirectory = true };
        var preview = await (await client.PostAsJsonAsync("/api/v1/ad/folders/preview", request)).Content.ReadFromJsonAsync<JsonElement>(Web);
        Assert.Equal($@"\\fs01\Projects\A\new{_base}", preview.GetProperty("unc").GetString());

        var steps = await (await client.PostAsJsonAsync("/api/v1/ad/folders", request)).Content.ReadFromJsonAsync<List<JsonElement>>(Web);
        Assert.All(steps!, s => Assert.Equal("Ok", s.GetProperty("status").GetString()));

        var acl = await client.GetFromJsonAsync<JsonElement>($"/api/v1/ad/folders/acl?path={Uri.EscapeDataString($@"A:\A\new{_base}")}", Web);
        Assert.True(acl.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task Permissions_and_validation()
    {
        var reader = await _factory.ClientWithPermissionsAsync(PermissionIds.AdFoldersRead);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync("/api/v1/ad/folders",
            new { projectDn = A, path = @"A:\A\x", baseName = "x", orgCode = "a", createDirectory = true })).StatusCode);
        var creator = await _factory.ClientWithPermissionsAsync(PermissionIds.AdFoldersCreate);
        Assert.Equal(HttpStatusCode.BadRequest, (await creator.PostAsJsonAsync("/api/v1/ad/folders",
            new { projectDn = A, path = @"Q:\x", baseName = "x", orgCode = "a", createDirectory = true })).StatusCode);
        var projects = await reader.GetFromJsonAsync<List<JsonElement>>("/api/v1/ad/projects", Web);
        Assert.Contains(projects!, p => p.GetProperty("name").GetString() == "A");
    }
}
