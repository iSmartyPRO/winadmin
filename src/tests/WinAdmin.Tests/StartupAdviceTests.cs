using WinAdmin.Api;

namespace WinAdmin.Tests;

public sealed class StartupAdviceTests
{
    [Fact]
    public void No_users_suggests_creating_administrator_with_role()
    {
        var lines = StartupAdvice.For(anyUsers: false, activeAdministrators: 0);
        Assert.Contains(lines, l => l.Contains("user add") && l.Contains("--role Администратор"));
        Assert.DoesNotContain(lines, l => l.Contains("--scopes"));
    }

    [Fact]
    public void Users_without_active_administrator_suggest_role_assign()
        => Assert.Contains(StartupAdvice.For(anyUsers: true, activeAdministrators: 0),
            l => l.Contains("role assign --role Администратор --local"));

    [Fact]
    public void Active_administrator_needs_no_advice()
        => Assert.Empty(StartupAdvice.For(anyUsers: true, activeAdministrators: 1));
}
