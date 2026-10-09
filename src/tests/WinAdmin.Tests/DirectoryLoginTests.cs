using WinAdmin.Core.ActiveDirectory;

namespace WinAdmin.Tests;

public sealed class DirectoryLoginTests
{
    [Theory]
    [InlineData("PCS\\ilias.aidar", "ilias.aidar", null)]
    [InlineData(" ilias.aidar ", "ilias.aidar", null)]
    [InlineData("Ilias.Aidar@pcs-msk.com", "Ilias.Aidar", "Ilias.Aidar@pcs-msk.com")]
    public void Parses_login_forms(string login, string sam, string? upn)
    {
        var (s, u) = DirectoryLogin.Parse(login);
        Assert.Equal(sam, s);
        Assert.Equal(upn, u);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("PCS\\")]
    [InlineData("@pcs")]
    public void Rejects_empty_logins(string login)
        => Assert.Throws<ArgumentException>(() => DirectoryLogin.Parse(login));
}

public sealed class LdapFilterTests
{
    [Theory]
    [InlineData("a*b", "a\\2ab")]
    [InlineData("(x)", "\\28x\\29")]
    [InlineData("a\\b", "a\\5cb")]
    [InlineData("nul\0", "nul\\00")]
    [InlineData("иван", "иван")]
    public void Escapes_special_characters(string value, string expected)
        => Assert.Equal(expected, LdapFilter.Escape(value));

    [Fact]
    public void Sid_is_encoded_as_escaped_bytes()
        => Assert.Equal("\\01\\01\\00\\00\\00\\00\\00\\05\\12\\00\\00\\00", LdapFilter.Sid("S-1-5-18"));

    [Fact]
    public void Invalid_sid_is_rejected()
        => Assert.Throws<ArgumentException>(() => LdapFilter.Sid("not-a-sid"));
}
