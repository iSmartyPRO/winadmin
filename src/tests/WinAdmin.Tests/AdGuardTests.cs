using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Operations;
using WinAdmin.Core.Security;

namespace WinAdmin.Tests;

public sealed class AdGuardTests
{
    private const string Root = "OU=Accounts,DC=pcs";
    private static readonly AdStructureSettings S = AdStructureSettings.Default with { RootOu = Root, HiddenOus = ["IT"] };
    private static readonly PermissionCatalog Catalog = new([.. BuiltInModules.All, new ScopedTestModule()]);

    private static IAccessContext Actor(params string[] projects) => new AccessContext(
        new PrincipalRef(PrincipalType.LocalUser, "u", []), "u",
        PermissionEvaluator.Evaluate([new RoleSnapshot("r", false,
            [new RoleGrant(ScopedTestModule.Read, projects.Length == 0 ? null : new ScopeDefinition(projects))])], Catalog));

    [Fact]
    public void Managed_object_returns_its_project()
        => Assert.Equal("OU=Проект,OU=Accounts,DC=pcs", AdGuard.EnsureManaged("CN=u,OU=Users,OU=Проект,OU=Accounts,DC=pcs", S));

    [Theory]
    [InlineData("CN=u,OU=Other,DC=pcs")]
    [InlineData("CN=u,OU=IT,OU=Accounts,DC=pcs")]
    [InlineData("CN=g,OU=Accounts,DC=pcs")]
    public void Outside_root_hidden_or_not_in_project_is_denied(string dn)
        => Assert.Throws<AccessDeniedException>(() => AdGuard.EnsureManaged(dn, S));

    [Fact]
    public void Missing_root_is_a_configuration_error()
        => Assert.Throws<InvalidOperationException>(() => AdGuard.EnsureManaged("CN=u,DC=pcs", AdStructureSettings.Default));

    [Fact]
    public void Scope_limits_projects()
    {
        var actor = Actor("OU=A,OU=Accounts,DC=pcs");
        AdGuard.EnsureInScope(actor, ScopedTestModule.Read, "OU=A,OU=Accounts,DC=pcs");
        Assert.Throws<AccessDeniedException>(() => AdGuard.EnsureInScope(actor, ScopedTestModule.Read, "OU=B,OU=Accounts,DC=pcs"));
        Assert.Throws<AccessDeniedException>(() => AdGuard.EnsureInScope(actor, PermissionIds.ServicesRead, "OU=A,OU=Accounts,DC=pcs"));
        AdGuard.EnsureInScope(Actor(), ScopedTestModule.Read, "OU=B,OU=Accounts,DC=pcs"); // без области — всё
    }

    [Fact]
    public void In_scope_projects_are_filtered()
    {
        var projects = new[] { new AdProject("OU=A,OU=Accounts,DC=pcs", "A"), new AdProject("OU=B,OU=Accounts,DC=pcs", "B") };
        Assert.Equal(["A"], AdGuard.InScopeProjects(Actor("ou=a, ou=accounts, dc=pcs"), ScopedTestModule.Read, projects).Select(p => p.Name));
        Assert.Empty(AdGuard.InScopeProjects(Actor("OU=A,OU=Accounts,DC=pcs"), PermissionIds.ServicesRead, projects));
    }
}

public sealed class ScenarioRunnerTests
{
    [Fact]
    public async Task Stops_after_failure_and_marks_rest_not_run()
    {
        var logged = new List<Exception>();
        var run = new ScenarioRunner(logged.Add);
        int executed = 0;
        Assert.True(await run.RunAsync("Шаг 1", () => { executed++; return Task.FromResult(StepResult.Done("ok")); }));
        Assert.True(await run.RunAsync("Шаг 2", () => Task.FromResult(StepResult.Skip("уже сделано"))));
        Assert.False(await run.RunAsync("Шаг 3", () => throw new AdWriteException(50, "Нет прав")));
        Assert.False(await run.RunAsync("Шаг 4", () => { executed++; return Task.FromResult(StepResult.Done()); }));

        Assert.Equal(1, executed);
        Assert.True(run.Failed);
        Assert.Equal([StepStatus.Ok, StepStatus.Skipped, StepStatus.Failed, StepStatus.Skipped], run.Steps.Select(s => s.Status));
        Assert.Equal("Нет прав", run.Steps[2].Message);
        Assert.Contains("не выполнялся", run.Steps[3].Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(logged); // ожидаемые ошибки не логируются как сбои
    }

    [Fact]
    public async Task Unexpected_exception_is_hidden_from_user_and_logged()
    {
        var logged = new List<Exception>();
        var run = new ScenarioRunner(logged.Add);
        await run.RunAsync("Шаг", () => throw new NullReferenceException("секретная деталь"));
        Assert.DoesNotContain("секретная", run.Steps[0].Message);
        Assert.Single(logged);
    }
}
