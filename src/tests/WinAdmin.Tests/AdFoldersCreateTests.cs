using WinAdmin.Core.Abstractions;
using WinAdmin.Core.Operations;
using WinAdmin.Core.Security;

namespace WinAdmin.Tests;

public sealed class AdFoldersCreateTests : AdFoldersTestBase
{
    private const string Unc = @"\\fs01\Projects\A\New";
    private static CreateFolderRequest Request(string path = @"A:\A\New", string baseName = "new", bool create = true)
        => new(A, path, baseName, "a", create);

    [Fact]
    public async Task Preview_shows_names_and_unc()
    {
        var p = await Service.PreviewAsync(All(PermissionIds.AdFoldersCreate), new CreateFolderRequest(A, @"A:\A\New", "new", null, true), default);
        Assert.Equal("sg_a_new_full", p.FullGroup);   // orgCode «a» из существующих групп проекта
        Assert.Equal(Unc, p.Unc);
        Assert.Equal(A, p.GroupsOuDn);
    }

    [Fact]
    public async Task Wizard_creates_groups_directory_and_acl()
    {
        var steps = await Service.CreateFolderAsync(All(PermissionIds.AdFoldersCreate), Request(), default);
        Assert.All(steps, s => Assert.Equal(StepStatus.Ok, s.Status));
        var full = Ad.Groups.Single(g => g.Name == "sg_a_new_full");
        Assert.Equal(@"A:\A\New;Full Access", full.Description);
        Assert.Equal(@"A:\A\New;Read Only", Ad.Groups.Single(g => g.Name == "sg_a_new_read").Description);
        Assert.Contains(Unc, Ntfs.Directories);
        Assert.Contains(full.Sid, Ntfs.Rules[Unc]);
        Assert.Equal("folder.create", Assert.Single(Audited).Action);
    }

    [Fact]
    public async Task Rerun_reuses_groups_and_skips_done_steps()
    {
        await Service.CreateFolderAsync(All(PermissionIds.AdFoldersCreate), Request(), default);
        var again = await Service.CreateFolderAsync(All(PermissionIds.AdFoldersCreate), Request(), default);
        Assert.All(again, s => Assert.Equal(StepStatus.Skipped, s.Status));
        Assert.Single(Ad.Groups, g => g.Name == "sg_a_new_full");
    }

    [Fact]
    public async Task Existing_group_for_other_path_fails_wizard()
    {
        Ad.AddGroup("sg_a_new_full", A).Description = @"A:\A\Other;Full Access";
        var steps = await Service.CreateFolderAsync(All(PermissionIds.AdFoldersCreate), Request(), default);
        Assert.Equal(StepStatus.Failed, steps[0].Status);
        Assert.Contains("другой папкой", steps[0].Message);
        Assert.DoesNotContain(Ntfs.Calls, c => c.StartsWith("Grant"));
    }

    [Theory]
    [InlineData(@"C:\Local\X", "new")]
    [InlineData(@"A:\A\..\Windows", "new")]
    [InlineData(@"A:\A\New", "новая")]
    public async Task Wizard_validates_before_any_write(string path, string baseName)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Service.CreateFolderAsync(All(PermissionIds.AdFoldersCreate), Request(path, baseName), default));
        Assert.Empty(Ad.Calls);
        Assert.Empty(Ntfs.Calls);
    }

    [Fact]
    public async Task Missing_directory_without_create_flag_fails_before_acl()
    {
        var steps = await Service.CreateFolderAsync(All(PermissionIds.AdFoldersCreate), Request(create: false), default);
        Assert.Equal(StepStatus.Failed, steps[2].Status);
        Assert.DoesNotContain(Ntfs.Calls, c => c.StartsWith("Grant"));
    }

    [Fact]
    public async Task Wizard_requires_create_permission_in_project_scope()
        => await Assert.ThrowsAsync<AccessDeniedException>(() =>
            Service.CreateFolderAsync(Actor((PermissionIds.AdFoldersCreate, [B])), Request(), default));

    [Fact]
    public async Task Inspect_and_fix_existing_folder_acl()
    {
        Ntfs.Directories.Add(@"\\fs01\Projects\A\Docs");
        var actor = All(PermissionIds.AdFoldersRead, PermissionIds.AdFoldersCreate);
        Assert.False((await Service.InspectAclAsync(actor, @"A:\A\Docs", default)).Ok);
        var steps = await Service.FixAclAsync(actor, @"A:\A\Docs", default);
        Assert.Equal(StepStatus.Ok, Assert.Single(steps).Status);
        Assert.True((await Service.InspectAclAsync(actor, @"A:\A\Docs", default)).Ok);
        Assert.Equal("folder.acl.fix", Audited.Last().Action);
    }

    [Fact]
    public async Task Fix_for_unknown_folder_is_404()
        => await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            Service.FixAclAsync(All(PermissionIds.AdFoldersCreate), @"A:\A\Nope", default));
}
