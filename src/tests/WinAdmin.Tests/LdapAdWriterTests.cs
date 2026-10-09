using System.DirectoryServices.Protocols;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Infrastructure.ActiveDirectory;

namespace WinAdmin.Tests;

public sealed class AdWriteRequestsTests
{
    [Fact]
    public void Password_is_quoted_utf16le()
        => Assert.Equal(Encoding.Unicode.GetBytes("\"Пароль1!\""), AdWriteRequests.PasswordValue("Пароль1!"));

    [Theory]
    [InlineData(512, false, 514)]
    [InlineData(514, true, 512)]
    [InlineData(66050, true, 66048)]
    [InlineData(512, true, 512)]
    public void Toggles_only_disable_bit(int uac, bool enabled, int expected)
        => Assert.Equal(expected, AdWriteRequests.ToggleDisabled(uac, enabled));

    [Fact]
    public void Rid_parent_and_rdn()
    {
        Assert.Equal("513", AdWriteRequests.Rid("S-1-5-21-1-2-3-513"));
        Assert.Equal(@"OU=Users,OU=Проект,DC=x", AdWriteRequests.Parent(@"CN=Иванов\, Пётр,OU=Users,OU=Проект,DC=x"));
        Assert.Equal(@"CN=Иванов\, Пётр", AdWriteRequests.Rdn(@"CN=Иванов\, Пётр,OU=Users,OU=Проект,DC=x"));
    }

    [Fact]
    public void Global_security_group_type()
        => Assert.Equal(unchecked((int)0x80000002).ToString(), AdWriteRequests.GlobalSecurityGroupType);

    [Theory]
    [InlineData(ResultCode.InsufficientAccessRights, "не хватает прав")]
    [InlineData(ResultCode.NoSuchObject, "не найден")]
    [InlineData(ResultCode.EntryAlreadyExists, "уже существует")]
    [InlineData(ResultCode.ConstraintViolation, "политике")]
    [InlineData(ResultCode.UnwillingToPerform, "отклонил")]
    public void Translates_ldap_errors_to_russian(ResultCode code, string fragment)
    {
        var ex = AdWriteRequests.Translate(code, "PCS\\svc", "изменение членства", "CN=g,DC=x", "00002098: SecErr: DSID-03150F94");
        var write = Assert.IsType<AdWriteException>(ex);
        Assert.Contains(fragment, write.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DSID", write.Message);
        Assert.Equal((int)code, write.Code);
    }

    [Theory]
    [InlineData(ResultCode.Busy)]
    [InlineData(ResultCode.Unavailable)]
    public void Server_trouble_is_unavailable(ResultCode code)
        => Assert.IsType<DirectoryUnavailableException>(AdWriteRequests.Translate(code, "svc", "op", "dn", null));
}

public sealed class LdapAdWriterTests
{
    private static LdapAdWriter Writer(DirectorySettings dir, AdWriteCredential cred)
    {
        var d = Mock.Of<IDirectorySettingsStore>(m => m.GetAsync(It.IsAny<CancellationToken>()) == Task.FromResult(dir));
        var s = new Mock<IAdStructureStore>();
        s.Setup(x => x.GetWriteCredentialAsync(It.IsAny<CancellationToken>())).ReturnsAsync(cred);
        return new LdapAdWriter(d, s.Object, NullLogger<LdapAdWriter>.Instance);
    }

    [Fact]
    public async Task Unconfigured_service_account_fails_before_network()
        => await Assert.ThrowsAsync<ArgumentException>(() =>
            Writer(new DirectorySettings(true, "x.local", "127.0.0.1", null, false), new AdWriteCredential(AdWriteMode.ServiceAccount, null, null))
                .SetEnabledAsync("CN=u,DC=x", true));

    [Fact]
    public async Task Unreachable_dc_is_unavailable()
        => await Assert.ThrowsAsync<DirectoryUnavailableException>(() =>
            Writer(new DirectorySettings(true, "x.local", "127.0.0.1", null, false), new AdWriteCredential(AdWriteMode.ServiceAccount, "svc", "pw"))
                .SetEnabledAsync("CN=u,DC=x", true));

    private static (LdapAdWriter Writer, string Root) Real()
    {
        string E(string n) => Environment.GetEnvironmentVariable(n) ?? "";
        return (Writer(new DirectorySettings(true, E("WINADMIN_TEST_AD_DOMAIN"), E("WINADMIN_TEST_AD_SERVER"), null, false),
            new AdWriteCredential(AdWriteMode.ServiceAccount, E("WINADMIN_TEST_AD_USER"), E("WINADMIN_TEST_AD_PASSWORD"))), E("WINADMIN_TEST_AD_ROOT"));
    }

    [AdFact]
    public async Task Real_directory_group_lifecycle_inside_test_ou()
    {
        var (w, root) = Real();
        string cn = "sg_winadmin_test_" + Guid.NewGuid().ToString("N")[..8];
        string groupDn = await w.CreateGroupAsync(root, cn, "A:\\WinAdmin-Test;Full Access");
        Assert.StartsWith("CN=" + cn, groupDn);
        await w.ModifyAttributesAsync(groupDn, new Dictionary<string, string?> { ["info"] = "created by test" });
        var ex = await Assert.ThrowsAsync<AdWriteException>(() => w.CreateGroupAsync(root, cn, "dup"));
        Assert.Equal((int)ResultCode.EntryAlreadyExists, ex.Code);
    }
}
