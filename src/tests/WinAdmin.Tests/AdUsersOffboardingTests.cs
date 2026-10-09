using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.Operations;
using WinAdmin.Core.Security;

namespace WinAdmin.Tests;

public sealed class AdUsersOffboardingTests : AdUsersServiceTestBase
{
    private string FiredDn => Ad.Groups.Single(g => g.Name == "Fired Users").Dn;

    [Fact]
    public async Task Deactivation_runs_access_steps_in_order()
    {
        var steps = await Service.DeactivateAsync(All(PermissionIds.AdUsersOffboard), "ivan", default);
        var ivan = Ad.U("ivan");

        Assert.All(steps, s => Assert.NotEqual(StepStatus.Failed, s.Status));
        Assert.Equal("CN=ivan," + Fired, ivan.Dn);
        Assert.False(ivan.Enabled);
        Assert.Equal(int.Parse(Ad.Groups.Single(g => g.Name == "Fired Users").Sid.Split('-')[^1]), ivan.PrimaryGroupId);
        Assert.DoesNotContain(Ad.Groups, g => g.Members.Contains(ivan.Dn) && g.Name != "Fired Users");
        Assert.Contains("Пользователи домена", string.Join("|", steps.Select(s => s.Name)));
        Assert.NotNull(ivan.Password);
        Assert.Equal(
            ["AddMember", "SetPrimary", "RemoveMember", "RemoveMember", "ResetPassword", "Disable", "Move"],
            Ad.Calls.Select(c => c.Split(':')[0]));
        var e = Assert.Single(Audited);
        Assert.Equal("user.account.deactivate", e.Action);
        Assert.DoesNotContain(ivan.Password!, e.Details);
    }

    [Fact]
    public async Task Deactivation_stops_on_failure_and_rerun_completes()
    {
        Ad.FailOn["RemoveMember:CN=sg_a_docs_full," + A] = new AdWriteException(50, "Нет прав");
        var first = await Service.DeactivateAsync(All(PermissionIds.AdUsersOffboard), "ivan", default);
        Assert.Contains(first, s => s.Status == StepStatus.Failed);
        Assert.True(Ad.U("ivan").Enabled);                         // до отключения не дошли
        Assert.Contains("не выполнялся", first[^1].Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Assert.Single(Audited).Success);

        Ad.FailOn.Clear();
        var second = await Service.DeactivateAsync(All(PermissionIds.AdUsersOffboard), "ivan", default);
        Assert.All(second, s => Assert.NotEqual(StepStatus.Failed, s.Status));
        Assert.Contains(second, s => s.Status == StepStatus.Skipped);  // уже в Fired Users
        Assert.False(Ad.U("ivan").Enabled);
    }

    [Fact]
    public async Task Repeat_deactivation_of_terminated_user_is_idempotent()
    {
        await Service.DeactivateAsync(All(PermissionIds.AdUsersOffboard), "ivan", default);
        var again = await Service.DeactivateAsync(Actor((PermissionIds.AdUsersOffboard, [B])), "ivan", default);
        Assert.All(again, s => Assert.NotEqual(StepStatus.Failed, s.Status));
        Assert.Equal("CN=ivan," + Fired, Ad.U("ivan").Dn);
    }

    [Fact]
    public async Task Deactivation_needs_settings_and_scope()
    {
        await Assert.ThrowsAsync<AccessDeniedException>(() => Service.DeactivateAsync(Actor((PermissionIds.AdUsersOffboard, [B])), "ivan", default));
        Settings.FiredGroup = "";
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service.DeactivateAsync(All(PermissionIds.AdUsersOffboard), "ivan", default));
        Assert.Empty(Ad.Calls);
    }

    [Fact]
    public async Task Activation_restores_into_project_and_returns_password_once()
    {
        await Service.DeactivateAsync(All(PermissionIds.AdUsersOffboard), "ivan", default);
        Ad.Calls.Clear();
        Audited.Clear();

        var result = await Service.ActivateAsync(Actor((PermissionIds.AdUsersOffboard, [B])), "ivan", B, default);
        var ivan = Ad.U("ivan");

        Assert.All(result.Steps, s => Assert.NotEqual(StepStatus.Failed, s.Status));
        Assert.Equal("CN=ivan,OU=Users," + B, ivan.Dn);
        Assert.True(ivan.Enabled);
        Assert.Equal(513, ivan.PrimaryGroupId);
        Assert.DoesNotContain(ivan.Dn, Ad.Groups.Single(g => g.Name == "Fired Users").Members);
        Assert.Equal(ivan.Password, result.Password);
        Assert.True(ivan.MustChange);
        Assert.DoesNotContain(result.Password!, Assert.Single(Audited).Details);
        Assert.Equal(["AddMember", "SetPrimary", "RemoveMember", "Move", "ResetPassword", "Enable"], Ad.Calls.Select(c => c.Split(':')[0]));
    }

    [Fact]
    public async Task Activation_target_must_be_in_scope()
    {
        await Service.DeactivateAsync(All(PermissionIds.AdUsersOffboard), "ivan", default);
        await Assert.ThrowsAsync<AccessDeniedException>(() => Service.ActivateAsync(Actor((PermissionIds.AdUsersOffboard, [A])), "ivan", B, default));
    }

    [Fact]
    public async Task Failed_activation_returns_no_password()
    {
        await Service.DeactivateAsync(All(PermissionIds.AdUsersOffboard), "ivan", default);
        Ad.FailOn["Move:CN=ivan," + Fired] = new AdWriteException(50, "Нет прав на перенос");
        var result = await Service.ActivateAsync(All(PermissionIds.AdUsersOffboard), "ivan", B, default);
        Assert.Null(result.Password);
        Assert.False(Ad.U("ivan").Enabled);
    }
}
