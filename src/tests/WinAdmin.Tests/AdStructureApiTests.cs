using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using WinAdmin.Core.Security;

namespace WinAdmin.Tests;

[Collection("network-api")]
public sealed class AdStructureApiTests(NetworkApiFactory factory)
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Settings_require_directory_permission()
    {
        var other = await factory.ClientWithPermissionsAsync(PermissionIds.PlatformRolesManage);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync("/api/v1/settings/ad")).StatusCode);
    }

    [Fact]
    public async Task Password_is_write_only_and_kept_when_omitted()
    {
        var client = await factory.ClientWithPermissionsAsync(PermissionIds.PlatformDirectoryManage);
        var put = await client.PutAsJsonAsync("/api/v1/settings/ad", new
        {
            rootOu = "OU=Accounts,DC=test,DC=local", usersOuName = "Users", hiddenOus = new[] { "IT" },
            writeMode = "ServiceAccount", writeLogin = "TEST\\svc", writePassword = "Svc-Pass-123!",
        });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        string body = await put.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Svc-Pass-123!", body);

        var again = await client.PutAsJsonAsync("/api/v1/settings/ad", new
        {
            rootOu = "OU=Accounts,DC=test,DC=local", usersOuName = "Staff", hiddenOus = Array.Empty<string>(),
            writeMode = "ServiceAccount", writeLogin = "TEST\\svc",
        });
        var json = await again.Content.ReadFromJsonAsync<JsonElement>(Web);
        Assert.True(json.GetProperty("hasWritePassword").GetBoolean());
        Assert.Equal("Staff", json.GetProperty("usersOuName").GetString());
    }

    [Fact]
    public async Task Invalid_root_is_400()
    {
        var client = await factory.ClientWithPermissionsAsync(PermissionIds.PlatformDirectoryManage);
        var r = await client.PutAsJsonAsync("/api/v1/settings/ad", new { rootOu = "DC=test", usersOuName = "Users", hiddenOus = Array.Empty<string>(), writeMode = "ProcessAccount" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Environment_check_returns_platform_report()
    {
        var client = await factory.ClientWithPermissionsAsync(PermissionIds.PlatformEnvironmentCheck);
        var reports = await client.GetFromJsonAsync<List<JsonElement>>("/api/v1/environment?depth=quick", Web);
        var platform = Assert.Single(reports!, r => r.GetProperty("moduleId").GetString() == "platform");
        Assert.Contains(platform.GetProperty("results").EnumerateArray(), r => r.GetProperty("code").GetString() == "ad.directory");
        Assert.NotEmpty((await client.GetFromJsonAsync<List<JsonElement>>("/api/v1/environment/latest", Web))!);

        var noRights = await factory.ClientWithPermissionsAsync(PermissionIds.ServicesRead);
        Assert.Equal(HttpStatusCode.Forbidden, (await noRights.GetAsync("/api/v1/environment")).StatusCode);
    }
}
