using WinAdmin.Core.Models;
using WinAdmin.Infrastructure.EventLogs;

namespace WinAdmin.Tests;

public sealed class EventLogQueryHelpersTests
{
    private static readonly DateTime Start = new(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 7, 11, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void BuildXPathFilter_TimeRangeOnly()
    {
        var request = new EventLogQueryRequest { LogName = "Security", StartTime = Start, EndTime = End };

        var xpath = EventLogQueryHelpers.BuildXPathFilter(request);

        Assert.Equal(
            "*[System[TimeCreated[@SystemTime>='2026-07-10T00:00:00.000Z' and @SystemTime<='2026-07-11T00:00:00.000Z']]]",
            xpath);
    }

    [Fact]
    public void BuildXPathFilter_WithEventIds()
    {
        var request = new EventLogQueryRequest
        {
            LogName = "Security", StartTime = Start, EndTime = End,
            EventIds = new[] { 4624, 4625 },
        };

        var xpath = EventLogQueryHelpers.BuildXPathFilter(request);

        Assert.Equal(
            "*[System[TimeCreated[@SystemTime>='2026-07-10T00:00:00.000Z' and @SystemTime<='2026-07-11T00:00:00.000Z'] and (EventID=4624 or EventID=4625)]]",
            xpath);
    }

    [Fact]
    public void BuildXPathFilter_WithLevels()
    {
        var request = new EventLogQueryRequest
        {
            LogName = "System", StartTime = Start, EndTime = End,
            Levels = new[] { "Error", "Warning" },
        };

        var xpath = EventLogQueryHelpers.BuildXPathFilter(request);

        Assert.Equal(
            "*[System[TimeCreated[@SystemTime>='2026-07-10T00:00:00.000Z' and @SystemTime<='2026-07-11T00:00:00.000Z'] and (Level=2 or Level=3)]]",
            xpath);
    }

    [Fact]
    public void BuildXPathFilter_WithEventIdsAndLevels()
    {
        var request = new EventLogQueryRequest
        {
            LogName = "Security", StartTime = Start, EndTime = End,
            EventIds = new[] { 4624 }, Levels = new[] { "Error" },
        };

        var xpath = EventLogQueryHelpers.BuildXPathFilter(request);

        Assert.Equal(
            "*[System[TimeCreated[@SystemTime>='2026-07-10T00:00:00.000Z' and @SystemTime<='2026-07-11T00:00:00.000Z'] and (EventID=4624) and (Level=2)]]",
            xpath);
    }

    [Fact]
    public void ExtractUserNameFromXml_ReturnsTargetUserName()
    {
        const string xml = """
            <Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'>
              <System><EventID>4624</EventID></System>
              <EventData>
                <Data Name='SubjectUserName'>WIN-SRV$</Data>
                <Data Name='TargetUserName'>ivan.petrov</Data>
              </EventData>
            </Event>
            """;

        Assert.Equal("ivan.petrov", EventLogQueryHelpers.ExtractUserNameFromXml(xml));
    }

    [Fact]
    public void ExtractUserNameFromXml_FallsBackToSubjectUserName_WhenTargetIsDash()
    {
        const string xml = """
            <Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'>
              <System><EventID>4625</EventID></System>
              <EventData>
                <Data Name='SubjectUserName'>SYSTEM</Data>
                <Data Name='TargetUserName'>-</Data>
              </EventData>
            </Event>
            """;

        Assert.Equal("SYSTEM", EventLogQueryHelpers.ExtractUserNameFromXml(xml));
    }

    [Fact]
    public void ExtractUserNameFromXml_FallsBackToAccountName()
    {
        const string xml = """
            <Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'>
              <System><EventID>1</EventID></System>
              <EventData>
                <Data Name='AccountName'>admin</Data>
              </EventData>
            </Event>
            """;

        Assert.Equal("admin", EventLogQueryHelpers.ExtractUserNameFromXml(xml));
    }

    [Fact]
    public void ExtractUserNameFromXml_ReturnsNull_WhenNoMatchingFields()
    {
        const string xml = """
            <Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'>
              <System><EventID>7036</EventID></System>
              <EventData>
                <Data Name='param1'>Print Spooler</Data>
                <Data Name='param2'>running</Data>
              </EventData>
            </Event>
            """;

        Assert.Null(EventLogQueryHelpers.ExtractUserNameFromXml(xml));
    }

    [Fact]
    public void ExtractUserNameFromXml_ReturnsNull_OnMalformedXml()
    {
        Assert.Null(EventLogQueryHelpers.ExtractUserNameFromXml("<Event><Unclosed>"));
    }

    [Theory]
    [InlineData("hello world", null, true)]
    [InlineData("hello world", "", true)]
    [InlineData("hello world", "WORLD", true)]
    [InlineData(null, "abc", false)]
    [InlineData("hello", "xyz", false)]
    public void MatchesSubstring_Cases(string? haystack, string? needle, bool expected)
    {
        Assert.Equal(expected, EventLogQueryHelpers.MatchesSubstring(haystack, needle));
    }

    [Fact]
    public void ParseIntList_ParsesCommaSeparated_AndSkipsNonNumeric()
    {
        var result = EventLogQueryHelpers.ParseIntList("4624,4625, 4634, abc");
        Assert.Equal(new[] { 4624, 4625, 4634 }, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseIntList_ReturnsNull_ForEmptyOrWhitespace(string? csv)
    {
        Assert.Null(EventLogQueryHelpers.ParseIntList(csv));
    }

    [Fact]
    public void ParseStringList_ParsesCommaSeparated_TrimsWhitespace()
    {
        var result = EventLogQueryHelpers.ParseStringList("Error, Warning ,Critical");
        Assert.Equal(new[] { "Error", "Warning", "Critical" }, result);
    }
}
