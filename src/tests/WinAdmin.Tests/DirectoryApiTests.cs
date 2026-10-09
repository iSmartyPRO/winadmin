using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.Security;

namespace WinAdmin.Tests;

[Collection("network-api")]
public sealed class DirectoryApiTests(NetworkApiFactory factory)
{
    [Fact]
    public async Task Directory_settings_require_directory_permission()
    {
        var other = await factory.ClientWithPermissionsAsync(PermissionIds.PlatformRolesManage);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync("/api/v1/settings/directory")).StatusCode);
    }

    [Fact]
    public async Task Settings_roundtrip_and_validation()
    {
        var saved = factory.DirectorySettings;
        try
        {
            var client = await factory.ClientWithPermissionsAsync(PermissionIds.PlatformDirectoryManage);
            var put = await client.PutAsJsonAsync("/api/v1/settings/directory",
                new { enabled = true, domain = "pcs-msk.com", server = "dc.pcs-msk.com", baseDn = (string?)null, useLdaps = false });
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
            var got = await client.GetFromJsonAsync<DirectorySettings>("/api/v1/settings/directory", new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.Equal("dc.pcs-msk.com", got!.Server);

            var bad = await client.PutAsJsonAsync("/api/v1/settings/directory", new { enabled = true, domain = "", useLdaps = false });
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        }
        finally { factory.DirectorySettings = saved; }
    }

    [Fact]
    public async Task Test_returns_steps()
    {
        var client = await factory.ClientWithPermissionsAsync(PermissionIds.PlatformDirectoryManage);
        var steps = await (await client.PostAsync("/api/v1/settings/directory/test", null)).Content.ReadFromJsonAsync<List<DirectoryTestStep>>();
        Assert.NotEmpty(steps!);
    }

    [Fact]
    public async Task Search_finds_groups_for_role_managers()
    {
        string name = "Search-" + Guid.NewGuid().ToString("N")[..6];
        factory.Directory.AddGroup(name);
        var client = await factory.ClientWithPermissionsAsync(PermissionIds.PlatformRolesManage);
        var found = await client.GetFromJsonAsync<List<JsonElement>>($"/api/v1/directory/search?q={name}&kind=group");
        Assert.Single(found!);
        Assert.StartsWith("S-1-5-21-", found![0].GetProperty("sid").GetString());

        var noRights = await factory.ClientWithPermissionsAsync(PermissionIds.ServicesRead);
        Assert.Equal(HttpStatusCode.Forbidden, (await noRights.GetAsync($"/api/v1/directory/search?q={name}")).StatusCode);
    }

    [Fact]
    public async Task Search_when_directory_is_down_returns_503()
    {
        var client = await factory.ClientWithPermissionsAsync(PermissionIds.PlatformRolesManage);
        factory.Directory.Down = true;
        try { Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/v1/directory/search?q=x")).StatusCode); }
        finally { factory.Directory.Down = false; }
    }
}
