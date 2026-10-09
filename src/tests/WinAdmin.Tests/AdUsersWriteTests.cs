using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure.ActiveDirectory.Users;

namespace WinAdmin.Tests;

public sealed class AdUsersWriteTests : AdUsersServiceTestBase
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];

    [Fact]
    public async Task Updates_only_changed_editable_attributes_and_audits_before_after()
    {
        Ad.U("ivan").Attributes["title"] = "Инженер";
        var view = await Service.UpdateAttributesAsync(All(PermissionIds.AdUsersEdit, PermissionIds.AdUsersRead), "ivan",
            new Dictionary<string, string?> { ["Title"] = " Главный инженер ", ["department"] = "ПТО", ["telephoneNumber"] = "" }, default);

        Assert.Equal("Главный инженер", Ad.U("ivan").Attributes["title"]);
        Assert.Equal("ПТО", view.Attributes["department"]);
        var e = Assert.Single(Audited);
        Assert.Equal("user.attributes.update", e.Action);
        Assert.Contains("title: «Инженер» → «Главный инженер»", e.Details);
        Assert.DoesNotContain("telephoneNumber", e.Details); // было пусто — стало пусто
    }

    [Fact]
    public async Task Unchanged_attributes_write_nothing()
    {
        Ad.U("ivan").Attributes["title"] = "Инженер";
        await Service.UpdateAttributesAsync(All(PermissionIds.AdUsersEdit), "ivan", new Dictionary<string, string?> { ["title"] = " Инженер " }, default);
        Assert.DoesNotContain(Ad.Calls, c => c.StartsWith("Modify"));
        Assert.Empty(Audited);
    }

    [Theory]
    [InlineData("userAccountControl", "512")]
    [InlineData("mail", "не-адрес")]
    public async Task Rejects_foreign_or_invalid_attributes(string name, string value)
        => await Assert.ThrowsAsync<ArgumentException>(() => Service.UpdateAttributesAsync(All(PermissionIds.AdUsersEdit), "ivan",
            new Dictionary<string, string?> { [name] = value }, default));

    [Fact]
    public async Task Too_long_value_is_rejected()
        => await Assert.ThrowsAsync<ArgumentException>(() => Service.UpdateAttributesAsync(All(PermissionIds.AdUsersEdit), "ivan",
            new Dictionary<string, string?> { ["title"] = new string('x', 257) }, default));

    [Fact]
    public async Task Edit_outside_scope_is_403_without_write()
    {
        await Assert.ThrowsAsync<AccessDeniedException>(() => Service.UpdateAttributesAsync(Actor((PermissionIds.AdUsersEdit, [B])), "ivan",
            new Dictionary<string, string?> { ["title"] = "x" }, default));
        Assert.Empty(Ad.Calls);
    }

    [Fact]
    public async Task Photo_type_and_size_are_checked()
    {
        var actor = All(PermissionIds.AdUsersEdit);
        await Service.SetPhotoAsync(actor, "ivan", Jpeg, default);
        Assert.Equal(Jpeg, Ad.U("ivan").Photo);
        await Assert.ThrowsAsync<ArgumentException>(() => Service.SetPhotoAsync(actor, "ivan", [1, 2, 3, 4], default));
        Settings.PhotoMaxKb = 1;
        await Assert.ThrowsAsync<ArgumentException>(() => Service.SetPhotoAsync(actor, "ivan", [.. Jpeg, .. new byte[2000]], default));
        await Service.SetPhotoAsync(actor, "ivan", null, default);
        Assert.Null(Ad.U("ivan").Photo);
        Assert.Equal(["user.photo.update", "user.photo.remove"], Audited.Select(a => a.Action));
    }

    [Fact]
    public async Task Move_needs_both_projects_in_scope()
    {
        await Assert.ThrowsAsync<AccessDeniedException>(() => Service.MoveAsync(Actor((PermissionIds.AdUsersMove, [A])), "ivan", B, default));
        var view = await Service.MoveAsync(Actor((PermissionIds.AdUsersMove, [A, B])), "ivan", B, default);
        Assert.Equal("CN=ivan,OU=Users," + B, Ad.U("ivan").Dn);
        Assert.Equal("B", view.ProjectName);
        Assert.Equal("user.move", Assert.Single(Audited).Action);
    }

    [Fact]
    public async Task Move_to_project_without_users_ou_is_409_and_nothing_written()
    {
        Reader.ExistingDns.Remove("OU=Users," + B);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service.MoveAsync(All(PermissionIds.AdUsersMove), "ivan", B, default));
        Assert.Empty(Ad.Calls);
    }

    [Fact]
    public async Task Move_to_hidden_project_is_denied()
        => await Assert.ThrowsAsync<AccessDeniedException>(() =>
            Service.MoveAsync(All(PermissionIds.AdUsersMove), "ivan", "OU=IT," + Root, default));

    [Fact]
    public async Task Password_generated_is_returned_but_not_audited()
    {
        string? generated = await Service.ResetPasswordAsync(All(PermissionIds.AdUsersPassword), "ivan", null, true, true, default);
        Assert.Equal(20, generated!.Length);
        Assert.Equal(generated, Ad.U("ivan").Password);
        Assert.True(Ad.U("ivan").MustChange);
        Assert.DoesNotContain(generated, Assert.Single(Audited).Details);
    }

    [Fact]
    public async Task Manual_password_must_be_long_enough()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Service.ResetPasswordAsync(All(PermissionIds.AdUsersPassword), "ivan", "short", false, false, default));
        Assert.Null(await Service.ResetPasswordAsync(All(PermissionIds.AdUsersPassword), "ivan", "Long-Enough-1", false, false, default));
        Assert.Equal("Long-Enough-1", Ad.U("ivan").Password);
    }

    [Fact]
    public async Task Ad_refusal_is_audited_as_failure_and_rethrown()
    {
        Ad.FailOn["ResetPassword:" + Ad.U("ivan").Dn] = new AdWriteException(19, "Пароль не соответствует политике домена");
        await Assert.ThrowsAsync<AdWriteException>(() => Service.ResetPasswordAsync(All(PermissionIds.AdUsersPassword), "ivan", "Long-Enough-1", false, false, default));
        Assert.False(Assert.Single(Audited).Success);
    }
}
