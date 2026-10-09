using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.Modules;
using WinAdmin.Core.Security;

namespace WinAdmin.Tests;

public sealed class DnUtilsTests
{
    private const string Root = "OU=Accounts,DC=pcs-msk,DC=com";

    [Fact]
    public void Splits_and_normalizes_with_case_and_spaces()
    {
        Assert.Equal(["OU=Проект", "DC=pcs"], DnUtils.Split("OU=Проект,DC=pcs"));
        Assert.Equal("OU=Проект,DC=pcs-msk,DC=com", DnUtils.Normalize("ou=Проект , dc=pcs-msk,  DC = com"));
    }

    [Fact]
    public void Escaped_comma_and_case()
    {
        const string dn = @"CN=Иванов\, Пётр,OU=Users,OU=Проект А,OU=Accounts,DC=PCS-MSK,DC=COM";
        Assert.Equal(5 + 1, DnUtils.Split(dn).Count);
        Assert.Equal("OU=Проект А,OU=Accounts,DC=PCS-MSK,DC=COM", DnUtils.ProjectDn(dn, Root));
        Assert.Equal(["Проект А", "Users"], DnUtils.RelativeOuPath(dn, Root));
        Assert.Equal("Иванов, Пётр", DnUtils.FirstValue(dn));
    }

    [Fact]
    public void Sibling_with_common_suffix_is_not_under()
    {
        Assert.False(DnUtils.IsUnderOrSame("OU=Users,OU=Project,DC=x", "OU=ject,DC=x"));
        Assert.False(DnUtils.IsUnderOrSame("OU=Pro,DC=x", "OU=Project,DC=x"));
        Assert.True(DnUtils.IsUnderOrSame("OU=Users,OU=Project,DC=x", "ou=project, dc=X"));
        Assert.True(DnUtils.IsUnderOrSame("OU=Project,DC=x", "OU=Project,DC=x"));
    }

    [Fact]
    public void Objects_outside_root_have_no_project()
    {
        Assert.Null(DnUtils.ProjectDn("CN=u,OU=Other,DC=pcs-msk,DC=com", Root));
        Assert.Null(DnUtils.ProjectDn(Root, Root));
        Assert.Null(DnUtils.ProjectDn("CN=Group,OU=Accounts,DC=pcs-msk,DC=com", Root)); // прямо в корне, не в проекте
        Assert.Empty(DnUtils.RelativeOuPath("CN=u,DC=other", Root));
    }
}

public sealed class AdStructureSettingsTests
{
    [Fact]
    public void Normalize_trims_and_defaults()
    {
        var s = new AdStructureSettings(" ou=Accounts, dc=pcs ", " ", [" IT ", "", "it"], AdWriteMode.ServiceAccount, "  ", false).Normalize();
        Assert.Equal("OU=Accounts,DC=pcs", s.RootOu);
        Assert.Equal("Users", s.UsersOuName);
        Assert.Equal(["IT"], s.HiddenOus);
        Assert.Null(s.WriteLogin);
    }

    [Theory]
    [InlineData("DC=pcs,DC=com")]
    [InlineData("CN=Users,DC=pcs")]
    [InlineData("не DN")]
    public void Root_must_be_an_ou(string root)
        => Assert.Throws<ArgumentException>(() =>
            (AdStructureSettings.Default with { RootOu = root }).Normalize());

    [Fact]
    public void Credential_does_not_print_password()
        => Assert.DoesNotContain("secret-pw", new AdWriteCredential(AdWriteMode.ServiceAccount, "PCS\\svc", "secret-pw").ToString());
}

public sealed class ProjectScopeProviderTests
{
    private readonly ProjectScopeProvider _p = new();

    [Fact]
    public void Normalizes_and_deduplicates()
        => Assert.Equal(["OU=Проект,OU=Accounts,DC=pcs"],
            _p.Normalize(new ScopeDefinition([" ou=Проект, ou=Accounts,dc=pcs", "OU=Проект,OU=Accounts,DC=pcs"])).Items);

    [Theory]
    [InlineData("Проект")]
    [InlineData("CN=x,DC=pcs")]
    public void Rejects_non_ou_items(string item)
        => Assert.Throws<ArgumentException>(() => _p.Normalize(new ScopeDefinition([item])));

    [Fact]
    public void Empty_scope_is_rejected()
        => Assert.Throws<ArgumentException>(() => _p.Normalize(new ScopeDefinition([" "])));

    [Fact]
    public void Nested_ou_is_subset_of_project()
    {
        var project = new ScopeDefinition(["OU=Проект,OU=Accounts,DC=pcs"]);
        Assert.True(_p.IsSubsetOf(new ScopeDefinition(["OU=Users,OU=Проект,OU=Accounts,DC=pcs"]), project));
        Assert.False(_p.IsSubsetOf(new ScopeDefinition(["OU=Другой,OU=Accounts,DC=pcs"]), project));
    }
}

public sealed class PasswordGeneratorTests
{
    [Fact]
    public void Generates_complex_unique_passwords()
    {
        var all = Enumerable.Range(0, 200).Select(_ => PasswordGenerator.Generate()).ToList();
        Assert.All(all, p =>
        {
            Assert.Equal(20, p.Length);
            Assert.Contains(p, char.IsUpper);
            Assert.Contains(p, char.IsLower);
            Assert.Contains(p, char.IsDigit);
            Assert.Contains(p, c => !char.IsLetterOrDigit(c));
        });
        Assert.Equal(all.Count, all.Distinct().Count());
    }

    [Fact]
    public void Too_short_length_is_rejected()
        => Assert.Throws<ArgumentOutOfRangeException>(() => PasswordGenerator.Generate(7));
}
