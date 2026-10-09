using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Api.Auth;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.Models;
using WinAdmin.Core.Security;

namespace WinAdmin.Tests;

[Collection("network-api")]
public sealed class WindowsSignInApiTests(NetworkApiFactory factory)
{
    private async Task<DirectoryObject> UserWithAccessAsync()
    {
        string sam = "sso" + Guid.NewGuid().ToString("N")[..6];
        var user = factory.Directory.AddUser(sam, "unused-pw");
        using var scope = factory.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<IRoleService>();
        var role = await roles.CreateAsync(new SaveRoleRequest("r-" + sam, null, [new RoleGrantDto(PermissionIds.ServicesRead, null)]), factory.SystemActor());
        await roles.AssignAsync(new CreateAssignmentRequest(role.Id, PrincipalType.AdUser, user.Sid, sam), factory.SystemActor());
        return user;
    }

    [Fact]
    public async Task Kerberos_sign_in_issues_token_even_over_plain_http_from_network()
    {
        var user = await UserWithAccessAsync();
        factory.Windows.Next = new WindowsSignIn(user.Sid, "Kerberos");
        var r = await factory.Remote().GetAsync("/api/v1/auth/windows");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("accessToken", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ntlm_is_refused()
    {
        var user = await UserWithAccessAsync();
        factory.Windows.Next = new WindowsSignIn(user.Sid, "NTLM");
        var r = await factory.Remote().GetAsync("/api/v1/auth/windows");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Contains("Kerberos", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Without_windows_identity_the_endpoint_challenges()
    {
        factory.Windows.Next = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.Remote().GetAsync("/api/v1/auth/windows")).StatusCode);
    }

    [Fact]
    public async Task Disabled_directory_hides_sso_and_options_say_so()
    {
        var saved = factory.DirectorySettings;
        factory.DirectorySettings = DirectorySettings.Disabled;
        try
        {
            Assert.Equal(HttpStatusCode.NotFound, (await factory.Remote().GetAsync("/api/v1/auth/windows")).StatusCode);
            var options = await factory.Remote().GetFromJsonAsync<Dictionary<string, bool>>("/api/v1/auth/options");
            Assert.False(options!["directory"]);
        }
        finally { factory.DirectorySettings = saved; }
        var on = await factory.Remote().GetFromJsonAsync<Dictionary<string, bool>>("/api/v1/auth/options");
        Assert.True(on!["directory"]);
    }
}
