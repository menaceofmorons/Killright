using Killright.UI.ClipboardMonitoring;
using Xunit;

namespace Killright.UI.Tests.Scan;

public sealed class PilotListParserTests
{
    [Theory]
    [InlineData("One Two Three Four")]
    [InlineData("A1 B2 C3 D4 E5 F6")]
    [InlineData("Abcdefghijklmnopqrstuvwxyzabc Pilot")]
    [InlineData("Pilot Abcdefghijklmnopqrstuvwxyz")]
    [InlineData("Abcdefghijklmnopqrstuvwxyzabcdefghij")]
    [InlineData("Ab -cd")]
    public void KeepsNamesThatMeetTheFourRules(string name)
    {
        Assert.Equal(new[] { name }, PilotListParser.ParseIfLikelyPilotList(name));
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("Abcdefghijklmnopqrstuvwxyzabcdefghijkl")]
    [InlineData("Pilot_One")]
    [InlineData("Pilot.One")]
    [InlineData("Pilot Ünder")]
    [InlineData("-Pilot")]
    [InlineData("Pilot-")]
    [InlineData("'Pilot")]
    [InlineData("Pilot  One")]
    public void DropsNamesThatBreakARule(string line)
    {
        Assert.Empty(PilotListParser.ParseIfLikelyPilotList(line));
    }

    [Fact]
    public void DeduplicatesCaseInsensitivelyKeepingFirstInInputOrder()
    {
        var result = PilotListParser.ParseIfLikelyPilotList("Bob Smith\r\nalice\n  BOB SMITH \nAlice\nzed");

        Assert.Equal(new[] { "Bob Smith", "alice", "zed" }, result);
    }

    [Fact]
    public void DropsBlankAndInvalidLinesSilentlyWhileKeepingOthers()
    {
        var result = PilotListParser.ParseIfLikelyPilotList("Valid One\n\nno!\nSecond");

        Assert.Equal(new[] { "Valid One", "Second" }, result);
    }
}
