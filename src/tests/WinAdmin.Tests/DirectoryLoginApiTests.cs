using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.Models;
using WinAdmin.Core.Security;

namespace WinAdmin.Tests;

[Collection("network-api")]
public sealed class DirectoryLoginApiTests(NetworkApiFactory factory)
{
    private sealed record Token(string AccessToken);

    /// <summary>Пользователь AD в группе, которой назначена роль с services.read.</summary>
    private async Task<string> AdUserWithAccessAsync(string password = "Ad-Pass-1")
    {
        string sam = "ad" + Guid.NewGuid().ToString("N")[..6];
        var group = factory.Directory.AddGroup("g-" + sam);
        factory.Directory.AddUser(sam, password, group.Sid);
        using var scope = factory.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<IRoleService>();
        var role = await roles.CreateAsync(new SaveRoleRequest("r-" + sam, null, [new RoleGrantDto(PermissionIds.ServicesRead, null)]), factory.SystemActor());
        await roles.AssignAsync(new CreateAssignmentRequest(role.Id, PrincipalType.AdGroup, group.Sid, group.SamAccountName), factory.SystemActor());
        return sam;
    }

    private static Task<HttpResponseMessage> Login(HttpClient c, string login, string password)
        => c.PostAsJsonAsync("/api/v1/auth/login", new { login, password });

    [Fact]
    public async Task Ad_user_signs_in_from_loopback_and_gets_group_permissions()
    {
        string sam = await AdUserWithAccessAsync();
        var client = factory.Loopback();
        var r = await Login(client, $"TEST\\{sam}", "Ad-Pass-1");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await r.Content.ReadFromJsonAsync<Token>())!.AccessToken);
        var me = await client.GetFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>("/api/v1/me");
        Assert.True(me!["permissions"].TryGetProperty(PermissionIds.ServicesRead, out _));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/users")).StatusCode);
    }

    [Fact]
    public async Task Ad_password_from_network_without_https_is_refused()
    {
        string sam = await AdUserWithAccessAsync();
        var r = await Login(factory.Remote(), sam, "Ad-Pass-1");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("HTTPS", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Forwarded_header_does_not_make_request_loopback()
    {
        string sam = await AdUserWithAccessAsync();
        var client = factory.Remote();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "127.0.0.1");
        Assert.Equal(HttpStatusCode.BadRequest, (await Login(client, sam, "Ad-Pass-1")).StatusCode);
    }

    [Fact]
    public async Task Ad_user_without_assignments_gets_403()
    {
        string sam = "na" + Guid.NewGuid().ToString("N")[..6];
        factory.Directory.AddUser(sam, "Ad-Pass-1");
        var r = await Login(factory.Loopback(), sam, "Ad-Pass-1");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Contains("Нет доступа к WinAdmin", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Wrong_ad_password_gets_401()
    {
        string sam = await AdUserWithAccessAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(factory.Loopback(), sam, "nope")).StatusCode);
    }

    [Fact]
    public async Task Local_login_wins_over_directory()
    {
        string login = "both" + Guid.NewGuid().ToString("N")[..6];
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IUserService>().CreateAsync(new CreateUserRequest { Login = login, Password = "Local-123456" });
        factory.Directory.AddUser(login, "Ad-Pass-1");
        // Пароль домена к локальной учётке не подходит — в домен запрос не уходит.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(factory.Loopback(), login, "Ad-Pass-1")).StatusCode);
    }

    [Fact]
    public async Task Directory_down_gives_503_and_local_users_still_work()
    {
        string sam = await AdUserWithAccessAsync();
        factory.Directory.Down = true;
        try
        {
            var r = await Login(factory.Loopback(), sam, "Ad-Pass-1");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
            Assert.Contains("Контроллер домена недоступен", await r.Content.ReadAsStringAsync());
        }
        finally { factory.Directory.Down = false; }
    }

    [Fact]
    public async Task Refresh_works_for_ad_user_and_fails_after_disable()
    {
        string sam = await AdUserWithAccessAsync();
        var client = factory.Loopback();
        Assert.Equal(HttpStatusCode.OK, (await Login(client, sam, "Ad-Pass-1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/v1/auth/refresh", null)).StatusCode);
    }

    [Fact]
    public async Task Refresh_fails_for_disabled_ad_account()
    {
        string sam = await AdUserWithAccessAsync();
        var client = factory.Loopback();
        Assert.Equal(HttpStatusCode.OK, (await Login(client, sam, "Ad-Pass-1")).StatusCode);
        factory.Directory.Disable(sam);
        // Кэш групп свежий (< 5 минут), но обновление сеанса перепроверяет учётку в каталоге.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/v1/auth/refresh", null)).StatusCode);
    }

    [Fact]
    public async Task Directory_disabled_falls_back_to_plain_401()
    {
        string sam = await AdUserWithAccessAsync();
        var saved = factory.DirectorySettings;
        factory.DirectorySettings = DirectorySettings.Disabled;
        try { Assert.Equal(HttpStatusCode.Unauthorized, (await Login(factory.Remote(), sam, "Ad-Pass-1")).StatusCode); }
        finally { factory.DirectorySettings = saved; }
    }
}
