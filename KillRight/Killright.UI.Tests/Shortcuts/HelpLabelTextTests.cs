using Killright.UI.Shortcuts;
using Xunit;

namespace Killright.UI.Tests.Shortcuts;

public sealed class HelpLabelTextTests
{
    [Theory]
    [InlineData("Medium: Likely link", "Medium:", " Likely link")]
    [InlineData("Very High: Many kills", "Very High:", " Many kills")]
    [InlineData("No label here", "", "No label here")]
    public void Split_SeparatesLeadingLabelAtFirstColon(string text, string label, string rest)
    {
        Assert.Equal((label, rest), HelpLabelText.Split(text));
    }
}
