using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using WinAdmin.Core.Security;

namespace WinAdmin.Tests;

[Collection("network-api")]
public sealed class PlatformApiTests(NetworkApiFactory factory)
{
    private static async Task<JsonNode> Json(HttpResponseMessage r) => JsonNode.Parse(await r.Content.ReadAsStringAsync())!;

    [Fact]
    public async Task Me_lists_permissions_and_enabled_modules()
    {
        var client = await factory.ClientWithPermissionsAsync(PermissionIds.ServicesRead);
        var me = await Json(await client.GetAsync("/api/v1/me"));

        Assert.True(me["permissions"]!.AsObject().ContainsKey(PermissionIds.ServicesRead));
        Assert.Null(me["permissions"]![PermissionIds.ServicesRead]);
        Assert.Contains(me["modules"]!.AsArray(), m => m!["id"]!.GetValue<string>() == "services");
    }

    [Fact]
    public async Task Admin_manages_roles_and_assignments_end_to_end()
    {
        var admin = await factory.ClientAsAdministratorAsync();

        var created = await admin.PostAsJsonAsync("/api/v1/roles",
            new { name = "Наблюдатель-" + Guid.NewGuid().ToString("N")[..4], permissions = new[] { new { permissionId = PermissionIds.ServicesRead } } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        string roleId = (await Json(created))["id"]!.GetValue<string>();

        var user = await admin.PostAsJsonAsync("/api/v1/users",
            new { login = "viewer-" + Guid.NewGuid().ToString("N")[..6], password = "Viewer-123456", roleIds = new[] { roleId } });
        Assert.Equal(HttpStatusCode.Created, user.StatusCode);
        var userJson = await Json(user);
        Assert.Contains(userJson["roles"]!.AsArray(), r => r!.GetValue<string>().StartsWith("Наблюдатель"));

        var assignments = await Json(await admin.GetAsync($"/api/v1/role-assignments?roleId={roleId}"));
        Assert.Single(assignments.AsArray());

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/roles/{roleId}")).StatusCode);
    }

    [Fact]
    public async Task Delegate_gets_403_with_violations_when_escalating()
    {
        var delegateClient = await factory.ClientWithPermissionsAsync(PermissionIds.PlatformRolesManage, PermissionIds.ServicesRead);
        var r = await delegateClient.PostAsJsonAsync("/api/v1/roles",
            new { name = "Эскалация", permissions = new[] { new { permissionId = PermissionIds.PowerManage } } });

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.NotEmpty((await Json(r))["violations"]!.AsArray());
    }

    [Fact]
    public async Task Modules_endpoint_lists_and_toggles()
    {
        var admin = await factory.ClientAsAdministratorAsync();
        var list = (await Json(await admin.GetAsync("/api/v1/modules"))).AsArray();
        Assert.Contains(list, m => m!["id"]!.GetValue<string>() == "power" && m["enabled"]!.GetValue<bool>());

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/api/v1/modules/power", new { enabled = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync("/api/v1/power/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/api/v1/modules/power", new { enabled = true })).StatusCode);
    }

    [Fact]
    public async Task Unknown_role_returns_404_and_bad_input_400()
    {
        var admin = await factory.ClientAsAdministratorAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync("/api/v1/roles/nope")).StatusCode);
        var bad = await admin.PostAsJsonAsync("/api/v1/roles", new { name = "", permissions = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }
}
