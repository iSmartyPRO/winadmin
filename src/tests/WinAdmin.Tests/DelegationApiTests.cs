using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Models;
using WinAdmin.Core.Security;

namespace WinAdmin.Tests;

/// <summary>Обходные пути повышения прав через пользователей и ключи.</summary>
[Collection("network-api")]
public sealed class DelegationApiTests(NetworkApiFactory factory)
{
    private async Task<string> CreateAdminUserAsync()
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserService>();
        var roles = scope.ServiceProvider.GetRequiredService<IRoleService>();
        var user = await users.CreateAsync(new CreateUserRequest { Login = "adm-" + Guid.NewGuid().ToString("N")[..6], Password = "Admin-123456" });
        await roles.AssignAsync(new CreateAssignmentRequest(BuiltInRoles.AdministratorId, PrincipalType.LocalUser, user.Id, null), factory.SystemActor());
        return user.Id;
    }

    [Fact]
    public async Task Users_manager_cannot_take_over_an_administrator()
    {
        string adminId = await CreateAdminUserAsync();
        var helpdesk = await factory.ClientWithPermissionsAsync(PermissionIds.PlatformUsersManage);

        var password = await helpdesk.PutAsJsonAsync($"/api/v1/users/{adminId}/password", new { newPassword = "Hijack-123456" });
        var disable = await helpdesk.PutAsJsonAsync($"/api/v1/users/{adminId}/active", new { isActive = false });
        var delete = await helpdesk.DeleteAsync($"/api/v1/users/{adminId}");

        Assert.Equal(HttpStatusCode.Forbidden, password.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, disable.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }

    [Fact]
    public async Task Granting_roles_on_create_requires_roles_permission()
    {
        string roleId;
        using (var scope = factory.Services.CreateScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<IRoleService>();
            roleId = (await roles.CreateAsync(new SaveRoleRequest("svc-" + Guid.NewGuid().ToString("N")[..6], null,
                [new RoleGrantDto(PermissionIds.ServicesRead, null)]), factory.SystemActor())).Id;
        }
        var keyManager = await factory.ClientWithPermissionsAsync(PermissionIds.PlatformApiKeysManage, PermissionIds.ServicesRead);
        var r = await keyManager.PostAsJsonAsync("/api/v1/apikeys", new { name = "k", roleIds = new[] { roleId } });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);

        var userManager = await factory.ClientWithPermissionsAsync(PermissionIds.PlatformUsersManage, PermissionIds.ServicesRead);
        var u = await userManager.PostAsJsonAsync("/api/v1/users",
            new { login = "u-" + Guid.NewGuid().ToString("N")[..6], password = "User-1234567", roleIds = new[] { roleId } });
        Assert.Equal(HttpStatusCode.Forbidden, u.StatusCode);
    }
}
