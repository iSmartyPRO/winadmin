using Moq;
using WinAdmin.Core.Abstractions;
using WinAdmin.Core.ActiveDirectory;
using WinAdmin.Infrastructure.ActiveDirectory;
using WinAdmin.Tests.Fakes;

namespace WinAdmin.Tests;

public sealed class AdGroupCacheTests
{
    private readonly FakeDirectory _ad = new();
    private readonly ManualTimeProvider _time = new(DateTimeOffset.UtcNow);
    private readonly AdGroupCache _cache;
    private readonly string _sid;
    private readonly string _group;
    private DirectorySettings _settings = new(true, "test.local", null, null, false);

    public AdGroupCacheTests()
    {
        _group = _ad.AddGroup("WinAdmin-Helpdesk").Sid;
        _sid = _ad.AddUser("ivan", "pw", _group).Sid;
        _cache = new AdGroupCache(_ad, Settings(), _time);
    }

    private IDirectorySettingsStore Settings()
    {
        var store = new Mock<IDirectorySettingsStore>();
        store.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => _settings);
        return store.Object;
    }

    [Fact]
    public async Task Switching_directory_off_ends_sessions_immediately()
    {
        Assert.NotNull(await _cache.GetGroupsAsync(_sid));
        _settings = DirectorySettings.Disabled;
        Assert.Null(await _cache.GetGroupsAsync(_sid)); // кэш свежий, но вход доменом выключен
    }

    [Fact]
    public async Task Reads_groups_once_per_five_minutes()
    {
        Assert.Equal([_group], await _cache.GetGroupsAsync(_sid));
        _time.Advance(TimeSpan.FromMinutes(4));
        await _cache.GetGroupsAsync(_sid);
        Assert.Equal(1, _ad.TokenGroupCalls);

        _time.Advance(TimeSpan.FromMinutes(2));
        await _cache.GetGroupsAsync(_sid);
        Assert.Equal(2, _ad.TokenGroupCalls);
    }

    [Fact]
    public async Task Keeps_last_groups_up_to_fifteen_minutes_when_directory_is_down()
    {
        await _cache.GetGroupsAsync(_sid);
        _ad.Down = true;
        _time.Advance(TimeSpan.FromMinutes(14));
        Assert.Equal([_group], await _cache.GetGroupsAsync(_sid));

        _time.Advance(TimeSpan.FromMinutes(2));
        Assert.Null(await _cache.GetGroupsAsync(_sid));
    }

    [Fact]
    public async Task Disabled_account_invalidates_session()
    {
        await _cache.GetGroupsAsync(_sid);
        _ad.Disable("ivan");
        _time.Advance(TimeSpan.FromMinutes(6));
        Assert.Null(await _cache.GetGroupsAsync(_sid));
    }

    [Fact]
    public async Task Unknown_sid_and_unreachable_directory_without_cache_give_null()
    {
        Assert.Null(await _cache.GetGroupsAsync("S-1-5-21-1-2-3-999"));
        _ad.Down = true;
        Assert.Null(await new AdGroupCache(_ad, Settings(), _time).GetGroupsAsync(_sid));
    }
}
