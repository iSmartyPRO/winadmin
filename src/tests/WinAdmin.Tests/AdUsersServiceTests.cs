using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.ActiveDirectory.Users;
using WinAdmin.Core.Models;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;
using WinAdmin.Infrastructure.ActiveDirectory.Users;
using WinAdmin.Tests.Fakes;

namespace WinAdmin.Tests;

/// <summary>Общая подготовка: корень OU=Accounts, проекты A и B, скрытый IT, OU уволенных вне корня.</summary>
public abstract class AdUsersServiceTestBase
{
    protected const string Root = "OU=Accounts,DC=test,DC=local";
    protected const string A = "OU=A," + Root;
    protected const string B = "OU=B," + Root;
    protected const string Fired = "OU=FiredUsers,DC=test,DC=local";
    protected static readonly PermissionCatalog Catalog = new(BuiltInModules.All);

    protected readonly FakeAdDomain Ad = new();
    protected readonly FakeAdReader Reader = new();
    protected readonly Mock<IAuditService> Audit = new();
    protected readonly List<AuditEntryDto> Audited = [];
    protected AdUsersSettings Settings = new() { FiredGroup = "Fired Users", TerminatedOuDn = Fired };
    protected AdStructureSettings Structure = AdStructureSettings.Default with { RootOu = Root, HiddenOus = ["IT"] };
    protected readonly AdUsersService Service;

    protected AdUsersServiceTestBase()
    {
        Reader.Projects.AddRange([new AdProject(A, "A"), new AdProject(B, "B")]);
        Reader.ExistingDns.UnionWith([Root, A, B, "OU=Users," + A, "OU=Users," + B, Fired]);
        Ad.AddGroup("Fired Users", "OU=Groups," + Root);
        Ad.AddGroup("sg_a_docs_full", A);
        Ad.AddUser("ivan", "OU=Users," + A, "CN=sg_a_docs_full," + A);
        Ad.AddUser("petr", "OU=Users," + B);
        Ad.AddUser("admin2", "OU=IT," + Root);
        Ad.AddUser("old", Fired);
        Ad.U("old").Enabled = false;
        Ad.DomainUsers.Members.Remove(Ad.U("old").Dn);

        var structure = new Mock<IAdStructureStore>();
        structure.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => Structure);
        var modules = new Mock<IModuleRegistry>();
        modules.Setup(m => m.GetSettingsAsync<AdUsersSettings>(AdUsersModule.ModuleId, It.IsAny<CancellationToken>())).ReturnsAsync(() => Settings);
        Audit.Setup(a => a.WriteAsync(It.IsAny<AuditEntryDto>(), It.IsAny<CancellationToken>()))
            .Callback<AuditEntryDto, CancellationToken>((e, _) => Audited.Add(e)).Returns(Task.CompletedTask);
        Audit.Setup(a => a.QueryAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int _, string? _, string? target, CancellationToken _) => Audited.Where(e => e.Target == target).ToList());
        Service = new AdUsersService(Ad, Ad, Reader, structure.Object, modules.Object, Audit.Object, NullLogger<AdUsersService>.Instance);
    }

    protected static IAccessContext Actor(params (string Permission, string[]? Projects)[] grants)
        => new AccessContext(new PrincipalRef(PrincipalType.LocalUser, "op", []), "op",
            PermissionEvaluator.Evaluate([new RoleSnapshot("r", false,
                grants.Select(g => new RoleGrant(g.Permission, g.Projects is null ? null : new ScopeDefinition(g.Projects))).ToList())], Catalog));

    protected static IAccessContext All(params string[] permissions)
        => Actor(permissions.Select(p => (p, (string[]?)null)).ToArray());
}

public sealed class AdUsersReadTests : AdUsersServiceTestBase
{
    [Fact]
    public async Task List_shows_managed_users_with_projects_and_hides_hidden_ou_and_terminated()
    {
        var list = await Service.ListAsync(All(PermissionIds.AdUsersRead), null, AdUserStatus.All, null, default);
        Assert.Equal(["ivan", "petr"], list.Select(u => u.Sam).Order());
        Assert.Equal("A", list.Single(u => u.Sam == "ivan").ProjectName);
    }

    [Fact]
    public async Task List_is_limited_to_scope()
    {
        var delegateA = Actor((PermissionIds.AdUsersRead, [A]));
        Assert.Equal(["ivan"], (await Service.ListAsync(delegateA, null, AdUserStatus.All, null, default)).Select(u => u.Sam));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Service.ListAsync(delegateA, B, AdUserStatus.All, null, default));
    }

    [Fact]
    public async Task Status_and_search_filters()
    {
        Ad.U("petr").Enabled = false;
        var actor = All(PermissionIds.AdUsersRead);
        Assert.Equal(["ivan"], (await Service.ListAsync(actor, null, AdUserStatus.Active, null, default)).Select(u => u.Sam));
        Assert.Equal(["petr"], (await Service.ListAsync(actor, null, AdUserStatus.Disabled, null, default)).Select(u => u.Sam));
        Assert.Equal(["ivan"], (await Service.ListAsync(actor, null, AdUserStatus.All, "IVA", default)).Select(u => u.Sam));
    }

    [Fact]
    public async Task Terminated_list_requires_offboard()
    {
        await Assert.ThrowsAsync<AccessDeniedException>(() =>
            Service.ListAsync(All(PermissionIds.AdUsersRead), null, AdUserStatus.Terminated, null, default));
        var list = await Service.ListAsync(Actor((PermissionIds.AdUsersRead, [A]), (PermissionIds.AdUsersOffboard, [A])),
            null, AdUserStatus.Terminated, null, default);
        Assert.True(Assert.Single(list).Terminated);
    }

    [Fact]
    public async Task Card_has_groups_with_primary_first()
    {
        var card = await Service.GetAsync(All(PermissionIds.AdUsersRead), "ivan", default);
        Assert.True(card.Groups[0].IsPrimary);
        Assert.Contains(card.Groups, g => g.Name == "sg_a_docs_full");
    }

    [Fact]
    public async Task User_outside_root_or_hidden_is_403_and_unknown_is_404()
    {
        var actor = All(PermissionIds.AdUsersRead);
        await Assert.ThrowsAsync<AccessDeniedException>(() => Service.GetAsync(actor, "admin2", default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Service.GetAsync(actor, "nobody", default));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Service.GetAsync(Actor((PermissionIds.AdUsersRead, [B])), "ivan", default));
    }

    [Fact]
    public async Task Missing_root_is_a_configuration_error()
    {
        Structure = Structure with { RootOu = null };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service.ListAsync(All(PermissionIds.AdUsersRead), null, AdUserStatus.All, null, default));
    }

    [Fact]
    public async Task History_is_audit_by_user_dn()
    {
        Audited.Add(new AuditEntryDto { Actor = "x", Action = "user.move", Target = Ad.U("ivan").Dn, Success = true });
        Audited.Add(new AuditEntryDto { Actor = "x", Action = "user.move", Target = "CN=other", Success = true });
        Assert.Single(await Service.HistoryAsync(All(PermissionIds.AdUsersRead), "ivan", default));
    }
}
