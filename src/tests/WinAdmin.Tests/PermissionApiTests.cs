using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Security;

namespace WinAdmin.Tests;

[Collection("network-api")]
public sealed class PermissionApiTests(NetworkApiFactory factory)
{
    [Fact]
    public async Task Permission_from_role_opens_endpoint_and_its_absence_returns_403()
    {
        var reader = await factory.ClientWithPermissionsAsync(PermissionIds.ServicesRead);
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync("/api/v1/services")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.GetAsync("/api/v1/processes")).StatusCode);
    }

    [Fact]
    public async Task Disabled_module_returns_404_even_without_permission()
    {
        var registry = factory.Services.GetRequiredService<IModuleRegistry>();
        await registry.SetEnabledAsync("printers", false, "test");
        try
        {
            var admin = await factory.ClientAsAdministratorAsync();
            Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/v1/printers")).StatusCode);
            var nobody = await factory.ClientWithPermissionsAsync(PermissionIds.ServicesRead);
            Assert.Equal(HttpStatusCode.NotFound, (await nobody.GetAsync("/api/v1/printers")).StatusCode);
        }
        finally
        {
            await registry.SetEnabledAsync("printers", true, "test");
        }
    }

    [Fact]
    public async Task Legacy_jwt_without_principal_claim_still_works()
    {
        // Пользователь-администратор и токен в старом формате (только NameIdentifier/Name).
        string userId;
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserService>();
            var created = await users.CreateAsync(new() { Login = "legacy-" + Guid.NewGuid().ToString("N")[..6], Password = "Legacy-123456" });
            userId = created.Id;
            var roles = scope.ServiceProvider.GetRequiredService<IRoleService>();
            await roles.AssignAsync(new(BuiltInRoles.AdministratorId, PrincipalType.LocalUser, userId, null), factory.SystemActor());
        }
        string token = factory.LegacyAccessToken(userId);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/services")).StatusCode);
    }
}
