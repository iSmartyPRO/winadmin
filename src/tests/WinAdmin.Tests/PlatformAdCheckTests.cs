using Moq;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Core.EnvironmentChecks;
using WinAdmin.Infrastructure.EnvironmentChecks;
using WinAdmin.Tests.Fakes;

namespace WinAdmin.Tests;

public sealed class PlatformAdCheckTests
{
    private const string Root = "OU=Accounts,DC=pcs";
    private readonly FakeDirectory _directory = new();
    private readonly FakeAdReader _reader = new();
    private DirectorySettings _dirSettings = new(true, "pcs", null, null, false);
    private AdStructureSettings _structure = AdStructureSettings.Default with
    {
        RootOu = Root, HiddenOus = ["IT"], WriteLogin = "PCS\\svc", HasWritePassword = true,
    };
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero));

    public PlatformAdCheckTests()
    {
        _reader.ExistingDns.Add(Root);
        _reader.ExistingDns.Add("OU=IT," + Root);
        _reader.Projects.Add(new AdProject("OU=A," + Root, "A"));
    }

    private async Task<Dictionary<string, CheckResult>> RunAsync()
    {
        var dir = new Mock<IDirectorySettingsStore>();
        dir.Setup(d => d.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => _dirSettings);
        var st = new Mock<IAdStructureStore>();
        st.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => _structure);
        var results = await new PlatformAdCheck(dir.Object, _directory, st.Object, _reader, _time).RunAsync(CheckDepth.Quick, default);
        return results.ToDictionary(r => r.Code);
    }

    [Fact]
    public async Task Healthy_environment_is_all_ok()
    {
        var r = await RunAsync();
        Assert.All(r.Values, x => Assert.Equal(CheckStatus.Ok, x.Status));
        Assert.Contains("1", r["ad.root"].Message); // число проектов
    }

    [Fact]
    public async Task Directory_off_fails_and_dependents_are_skipped()
    {
        _dirSettings = DirectorySettings.Disabled;
        var r = await RunAsync();
        Assert.Equal(CheckStatus.Failed, r["ad.directory"].Status);
        Assert.NotNull(r["ad.directory"].Fix);
        Assert.Equal(CheckStatus.Skipped, r["ad.root"].Status);
        Assert.Equal(CheckStatus.Skipped, r["ad.writer"].Status);
        Assert.Equal(CheckStatus.Skipped, r["ad.channel"].Status);
    }

    [Fact]
    public async Task Missing_root_fails_with_fix()
    {
        _structure = _structure with { RootOu = null };
        var r = await RunAsync();
        Assert.Equal(CheckStatus.Failed, r["ad.root"].Status);
        Assert.Contains("Active Directory", r["ad.root"].Fix);
    }

    [Fact]
    public async Task Missing_hidden_ou_is_a_warning()
    {
        _reader.ExistingDns.Remove("OU=IT," + Root);
        Assert.Equal(CheckStatus.Warning, (await RunAsync())["ad.root"].Status);
    }

    [Fact]
    public async Task Writer_problems()
    {
        _reader.Writer = new("PCS\\svc", false, "Неверный логин или пароль", null, null, null, false);
        var r = await RunAsync();
        Assert.Equal(CheckStatus.Failed, r["ad.writer"].Status);
        Assert.Equal(CheckStatus.Skipped, r["ad.channel"].Status);

        _reader.Writer = new("PCS\\svc", true, null, true, false, _time.GetUtcNow().AddDays(5), true);
        Assert.Equal(CheckStatus.Warning, (await RunAsync())["ad.writer"].Status);

        _reader.Writer = new("PCS\\svc", true, null, false, false, null, true);
        Assert.Equal(CheckStatus.Failed, (await RunAsync())["ad.writer"].Status);
    }

    [Fact]
    public async Task Unencrypted_channel_fails()
    {
        _reader.Writer = _reader.Writer with { Encrypted = false };
        Assert.Equal(CheckStatus.Failed, (await RunAsync())["ad.channel"].Status);
    }

    [Fact]
    public async Task Undecryptable_password_is_reported()
    {
        _reader.Writer = new("?", false, "Пароль учётки записи не расшифровывается (ключ шифрования изменён или повреждён) — задайте его заново", null, null, null, false);
        var r = await RunAsync();
        Assert.Equal(CheckStatus.Failed, r["ad.writer"].Status);
        Assert.Contains("расшифровывается", r["ad.writer"].Message);
    }

    [Fact]
    public async Task Service_account_without_password_fails_without_bind()
    {
        _structure = _structure with { HasWritePassword = false };
        var r = await RunAsync();
        Assert.Equal(CheckStatus.Failed, r["ad.writer"].Status);
        Assert.Contains("пароль", r["ad.writer"].Message, StringComparison.OrdinalIgnoreCase);
    }
}
