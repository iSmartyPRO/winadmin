using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.ActiveDirectory.Folders;
using WinAdmin.Core.Models;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Operations;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure.ActiveDirectory.Folders;
using WinAdmin.Tests.Fakes;

namespace WinAdmin.Tests;

public abstract class AdFoldersTestBase
{
    protected const string Root = "OU=Accounts,DC=test,DC=local";
    protected const string A = "OU=A," + Root;
    protected const string B = "OU=B," + Root;
    protected static readonly PermissionCatalog Catalog = new(BuiltInModules.All);

    protected readonly FakeAdDomain Ad = new();
    protected readonly FakeAdReader Reader = new();
    protected readonly FakeNtfs Ntfs = new();
    protected readonly List<AuditEntryDto> Audited = [];
    protected AdFoldersSettings Settings = new() { DriveMappings = [@"A=\\fs01\Projects"] };
    protected readonly AdFoldersService Service;
    protected readonly FakeAdDomain.Group DocsFull, DocsRead;

    protected AdFoldersTestBase()
    {
        Reader.Projects.AddRange([new AdProject(A, "A"), new AdProject(B, "B")]);
        Reader.ExistingDns.UnionWith([Root, A, B]);
        DocsFull = Ad.AddGroup("sg_a_docs_full", A);
        DocsFull.Description = @"A:\A\Docs;Full Access";
        DocsRead = Ad.AddGroup("sg_a_docs_read", A);
        DocsRead.Description = @"A:\A\Docs;Read Only";
        Ad.AddGroup("sg_b_x_full", B).Description = @"A:\B\X;Full Access";
        Ad.AddGroup("sg_it_full", "OU=IT," + Root).Description = @"A:\IT;Full Access";
        Ad.AddGroup("sg_outside_full", "OU=Other,DC=test,DC=local").Description = @"A:\Out;Full Access";
        Ad.AddUser("ivan", "OU=Users," + A, DocsRead.Dn);

        var structureSettings = AdStructureSettings.Default with { RootOu = Root, HiddenOus = ["IT"] };
        var structure = Mock.Of<IAdStructureStore>(m => m.GetAsync(It.IsAny<CancellationToken>()) == Task.FromResult(structureSettings));
        var modules = new Mock<IModuleRegistry>();
        modules.Setup(m => m.GetSettingsAsync<AdFoldersSettings>(AdFoldersModule.ModuleId, It.IsAny<CancellationToken>())).ReturnsAsync(() => Settings);
        var audit = new Mock<IAuditService>();
        audit.Setup(a => a.WriteAsync(It.IsAny<AuditEntryDto>(), It.IsAny<CancellationToken>()))
            .Callback<AuditEntryDto, CancellationToken>((e, _) => Audited.Add(e)).Returns(Task.CompletedTask);
        Service = new AdFoldersService(Ad, Ad, Ad, Reader, Ntfs, structure, modules.Object, new FolderCatalogCache(),
            audit.Object, NullLogger<AdFoldersService>.Instance);
    }

    protected static IAccessContext Actor(params (string Permission, string[]? Projects)[] grants)
        => new AccessContext(new PrincipalRef(PrincipalType.LocalUser, "op", []), "op",
            PermissionEvaluator.Evaluate([new RoleSnapshot("r", false,
                grants.Select(g => new RoleGrant(g.Permission, g.Projects is null ? null : new ScopeDefinition(g.Projects))).ToList())], Catalog));

    protected static IAccessContext All(params string[] permissions) => Actor(permissions.Select(p => (p, (string[]?)null)).ToArray());
}

public sealed class AdFoldersServiceTests : AdFoldersTestBase
{
    [Fact]
    public async Task Catalog_shows_managed_projects_only()
    {
        var result = await Service.ListAsync(All(PermissionIds.AdFoldersRead), null, null, default);
        Assert.Equal([@"A:\A\Docs", @"A:\B\X"], result.Folders.Select(f => f.Path));
        Assert.Equal("ivan", Assert.Single(result.Folders[0].Read!.Members).Sam);
    }

    [Fact]
    public async Task Catalog_is_limited_to_scope_and_searchable()
    {
        var delegateB = Actor((PermissionIds.AdFoldersRead, [B]));
        Assert.Equal([@"A:\B\X"], (await Service.ListAsync(delegateB, null, null, default)).Folders.Select(f => f.Path));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Service.ListAsync(delegateB, A, null, default));
        Assert.Single((await Service.ListAsync(All(PermissionIds.AdFoldersRead), null, "docs", default)).Folders);
    }

    [Fact]
    public async Task User_access_lists_folders()
    {
        var access = await Service.UserAccessAsync(All(PermissionIds.AdFoldersRead), "ivan", default);
        var a = Assert.Single(access);
        Assert.True(a.HasRead);
        Assert.False(a.HasFull);
    }

    [Fact]
    public async Task Adding_to_full_removes_from_read_of_same_folder()
    {
        var steps = await Service.ChangeMembershipAsync(All(PermissionIds.AdFoldersMembership),
            new MembershipRequest(DocsFull.Dn, "ivan", Add: true), default);
        var ivan = Ad.U("ivan").Dn;
        Assert.Contains(ivan, DocsFull.Members);
        Assert.DoesNotContain(ivan, DocsRead.Members);
        Assert.Equal(2, steps.Count);
        Assert.All(steps, s => Assert.Equal(StepStatus.Ok, s.Status));
        Assert.Equal(["group.member.add", "group.member.remove"], Audited.Select(a => a.Action));
    }

    [Fact]
    public async Task Add_without_remove_from_other_and_already_member_is_skipped()
    {
        var steps = await Service.ChangeMembershipAsync(All(PermissionIds.AdFoldersMembership),
            new MembershipRequest(DocsRead.Dn, "ivan", Add: true, RemoveFromOther: false), default);
        Assert.Equal(StepStatus.Skipped, Assert.Single(steps).Status);
    }

    [Fact]
    public async Task Membership_outside_zone_or_scope_is_denied_without_write()
    {
        var actor = All(PermissionIds.AdFoldersMembership);
        foreach (var name in new[] { "sg_it_full", "sg_outside_full" })
            await Assert.ThrowsAsync<AccessDeniedException>(() => Service.ChangeMembershipAsync(actor,
                new MembershipRequest(Ad.Groups.Single(g => g.Name == name).Dn, "ivan", true), default));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Service.ChangeMembershipAsync(Actor((PermissionIds.AdFoldersMembership, [B])),
            new MembershipRequest(DocsFull.Dn, "ivan", true), default));
        Assert.Empty(Ad.Calls);
    }

    [Fact]
    public async Task Non_prefixed_or_distribution_group_is_rejected()
    {
        var other = Ad.AddGroup("Mail All", A);
        await Assert.ThrowsAsync<AccessDeniedException>(() => Service.ChangeMembershipAsync(All(PermissionIds.AdFoldersMembership),
            new MembershipRequest(other.Dn, "ivan", true), default));
        DocsFull.IsSecurity = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service.ChangeMembershipAsync(All(PermissionIds.AdFoldersMembership),
            new MembershipRequest(DocsFull.Dn, "ivan", true), default));
    }

    [Fact]
    public async Task Unknown_member_is_404()
        => await Assert.ThrowsAsync<KeyNotFoundException>(() => Service.ChangeMembershipAsync(All(PermissionIds.AdFoldersMembership),
            new MembershipRequest(DocsFull.Dn, "nobody", true), default));

    [Fact]
    public async Task Catalog_cache_is_invalidated_by_membership_change()
    {
        var actor = All(PermissionIds.AdFoldersRead, PermissionIds.AdFoldersMembership);
        await Service.ListAsync(actor, null, null, default);
        await Service.ChangeMembershipAsync(actor, new MembershipRequest(DocsFull.Dn, "ivan", true), default);
        var docs = (await Service.ListAsync(actor, null, null, default)).Folders.Single(f => f.Path == @"A:\A\Docs");
        Assert.Equal("ivan", Assert.Single(docs.Full!.Members).Sam);
    }
}
