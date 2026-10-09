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

    public AdGroupCacheTests()
    {
        _group = _ad.AddGroup("WinAdmin-Helpdesk").Sid;
        _sid = _ad.AddUser("ivan", "pw", _group).Sid;
        _cache = new AdGroupCache(_ad, _time);
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
        Assert.Null(await new AdGroupCache(_ad, _time).GetGroupsAsync(_sid));
    }
}
