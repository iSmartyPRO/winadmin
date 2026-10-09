using System.Security.AccessControl;
using System.Security.Principal;
using Moq;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.ActiveDirectory.Users;
using WinAdmin.Core.EnvironmentChecks;
using WinAdmin.Infrastructure.ActiveDirectory;
using WinAdmin.Infrastructure.EnvironmentChecks;
using WinAdmin.Tests.Fakes;

namespace WinAdmin.Tests;

public sealed class AclInspectorTests
{
    private const string Writer = "S-1-5-21-7-8-9-1500";

    private static byte[] Sd(params GenericAce[] aces)
    {
        var acl = new RawAcl(GenericAcl.AclRevisionDS, aces.Length);
        for (int i = 0; i < aces.Length; i++) acl.InsertAce(i, aces[i]);
        var sd = new RawSecurityDescriptor(ControlFlags.DiscretionaryAclPresent, null, null, null, acl);
        var bytes = new byte[sd.BinaryLength];
        sd.GetBinaryForm(bytes, 0);
        return bytes;
    }

    private static ObjectAce Right(AceQualifier q, string sid, Guid right, AceFlags flags = AceFlags.None)
        => new(flags, q, 0x100 /* ControlAccess */, new SecurityIdentifier(sid), ObjectAceFlags.ObjectAceTypePresent, right, Guid.Empty, false, null);

    [Fact]
    public void Allow_for_writer_or_its_group_grants()
    {
        Assert.True(AclInspector.HasExtendedRight(Sd(Right(AceQualifier.AccessAllowed, Writer, AclInspector.ResetPassword)), [Writer], AclInspector.ResetPassword));
        Assert.False(AclInspector.HasExtendedRight(Sd(Right(AceQualifier.AccessAllowed, "S-1-5-21-7-8-9-999", AclInspector.ResetPassword)), [Writer], AclInspector.ResetPassword));
        Assert.False(AclInspector.HasExtendedRight(Sd(Right(AceQualifier.AccessAllowed, Writer, Guid.NewGuid())), [Writer], AclInspector.ResetPassword));
    }

    [Fact]
    public void Deny_wins_and_inherit_only_is_ignored()
    {
        Assert.False(AclInspector.HasExtendedRight(Sd(
            Right(AceQualifier.AccessDenied, Writer, AclInspector.ResetPassword),
            Right(AceQualifier.AccessAllowed, Writer, AclInspector.ResetPassword)), [Writer], AclInspector.ResetPassword));
        Assert.False(AclInspector.HasExtendedRight(Sd(
            Right(AceQualifier.AccessAllowed, Writer, AclInspector.ResetPassword, AceFlags.InheritOnly | AceFlags.ContainerInherit)), [Writer], AclInspector.ResetPassword));
    }

    [Fact]
    public void Full_control_grants_everything()
        => Assert.True(AclInspector.HasExtendedRight(Sd(new CommonAce(AceFlags.None, AceQualifier.AccessAllowed, 0x000F01FF,
            new SecurityIdentifier(Writer), false, null)), [Writer], AclInspector.ResetPassword));
}

public sealed class AdUsersCheckTests
{
    private const string Root = "OU=Accounts,DC=test,DC=local";
    private const string A = "OU=A," + Root;
    private readonly FakeAdDomain _ad = new();
    private readonly FakeAdReader _reader = new();
    private AdUsersSettings _settings = new() { FiredGroup = "Fired Users", TerminatedOuDn = "OU=Fired,DC=test,DC=local" };
    private AdStructureSettings _structure = AdStructureSettings.Default with { RootOu = Root, WriteLogin = "TEST\\svc", HasWritePassword = true };

    public AdUsersCheckTests()
    {
        _reader.Projects.Add(new AdProject(A, "A"));
        _reader.ExistingDns.UnionWith([Root, A, "OU=Users," + A, "OU=Fired,DC=test,DC=local"]);
        var fired = _ad.AddGroup("Fired Users", Root);
        var ivan = _ad.AddUser("ivan", "OU=Users," + A);
        var all = new HashSet<string>(AdUsersSettings.DefaultAttributes.Append("thumbnailPhoto"), StringComparer.OrdinalIgnoreCase);
        _reader.Effective[ivan.Dn] = new EffectiveRights(all, new HashSet<string>());
        _reader.Effective[fired.Dn] = new EffectiveRights(new HashSet<string> { "member" }, new HashSet<string>());
        _reader.Effective[_ad.DomainUsers.Dn] = new EffectiveRights(new HashSet<string> { "member" }, new HashSet<string>());
        _reader.Effective["OU=Fired,DC=test,DC=local"] = new EffectiveRights(new HashSet<string>(), new HashSet<string> { "user" });
        _ad.SecurityDescriptors[ivan.Dn] = AclSd(_ad.WriterSids[2]);
    }

    private static byte[] AclSd(string sid)
    {
        var acl = new RawAcl(GenericAcl.AclRevisionDS, 1);
        acl.InsertAce(0, new ObjectAce(AceFlags.None, AceQualifier.AccessAllowed, 0x100, new SecurityIdentifier(sid),
            ObjectAceFlags.ObjectAceTypePresent, AclInspector.ResetPassword, Guid.Empty, false, null));
        var sd = new RawSecurityDescriptor(ControlFlags.DiscretionaryAclPresent, null, null, null, acl);
        var bytes = new byte[sd.BinaryLength];
        sd.GetBinaryForm(bytes, 0);
        return bytes;
    }

    private async Task<Dictionary<string, CheckResult>> RunAsync()
    {
        var st = new Mock<IAdStructureStore>();
        st.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => _structure);
        var modules = new Mock<IModuleRegistry>();
        modules.Setup(m => m.GetSettingsAsync<AdUsersSettings>(AdUsersModule.ModuleId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _settings);
        return (await new AdUsersCheck(_reader, _ad, st.Object, modules.Object).RunAsync(CheckDepth.Quick, default)).ToDictionary(r => r.Code);
    }

    [Fact]
    public async Task Healthy_is_all_ok()
        => Assert.All((await RunAsync()).Values, r => Assert.Equal(CheckStatus.Ok, r.Status));

    [Fact]
    public async Task Missing_attribute_rights_are_listed_with_fix()
    {
        _reader.Effective[_ad.U("ivan").Dn] = new EffectiveRights(new HashSet<string> { "displayName" }, new HashSet<string>());
        var r = (await RunAsync())["ad.rights.users"];
        Assert.Equal(CheckStatus.Failed, r.Status);
        Assert.Contains("thumbnailPhoto", r.Message);
        Assert.Contains("PCS\\svc", r.Fix); // имя — из статуса учётки записи
    }

    [Fact]
    public async Task Missing_fired_group_and_ou_settings()
    {
        _settings = new AdUsersSettings();
        var r = await RunAsync();
        Assert.Equal(CheckStatus.Warning, r["users.fired"].Status);
        Assert.Equal(CheckStatus.Warning, r["users.terminatedOu"].Status);
    }

    [Fact]
    public async Task No_reset_password_right_fails()
    {
        _ad.SecurityDescriptors[_ad.U("ivan").Dn] = AclSd("S-1-5-21-1-1-1-1");
        Assert.Equal(CheckStatus.Failed, (await RunAsync())["users.resetPassword"].Status);
    }

    [Fact]
    public async Task Project_without_users_ou_is_a_warning()
    {
        _reader.Projects.Add(new AdProject("OU=B," + Root, "B"));
        var r = (await RunAsync())["users.usersOu"];
        Assert.Equal(CheckStatus.Warning, r.Status);
        Assert.Contains("B", r.Message);
    }

    [Fact]
    public async Task Writer_not_signed_in_skips_rights()
    {
        _reader.Writer = _reader.Writer with { Bound = false, Error = "нет" };
        var r = await RunAsync();
        Assert.Equal(CheckStatus.Skipped, r["ad.rights.users"].Status);
        Assert.Equal(CheckStatus.Skipped, r["users.resetPassword"].Status);
    }

    [Fact]
    public async Task No_root_skips_module_checks()
    {
        _structure = _structure with { RootOu = null };
        var r = await RunAsync();
        Assert.All(r.Values, x => Assert.Equal(CheckStatus.Skipped, x.Status));
    }
}
