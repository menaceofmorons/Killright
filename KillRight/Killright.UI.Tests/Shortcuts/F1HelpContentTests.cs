using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Killright.UI.Configuration;
using Killright.UI.Resources;
using Killright.UI.Shortcuts;
using Xunit;

namespace Killright.UI.Tests.Shortcuts;

public sealed class F1HelpContentTests
{
    private static F1HelpContent Create(ApplicationSettings? settings = null, string version = "1.2.3") =>
        F1HelpContent.Create(settings ?? new ApplicationSettings(), version);

    [Fact]
    public void StyleSizes_UseTheApplicationsOwnValues()
    {
        var settings = new ApplicationSettings
        {
            Style = new StyleSettings { BlobMinimumAverageAttackers = 6, FleetMinimumAverageAttackers = 14 }
        };

        var content = Create(settings);

        Assert.Contains("fewer than 6", content.StyleGang);
        Assert.Contains("6 to fewer than 14", content.StyleBlob);
        Assert.Contains("14 or more", content.StyleFleet);
    }

    [Fact]
    public void StyleSizes_MatchTheDefaultsTheClassifierUses()
    {
        var content = Create();

        Assert.Contains("fewer than 5", content.StyleGang);
        Assert.Contains("5 to fewer than 11", content.StyleBlob);
        Assert.Contains("11 or more", content.StyleFleet);
    }

    [Fact]
    public void ThreatExplanation_ShowsEveryBandRangeFromTheSettings()
    {
        var content = Create();

        Assert.Contains("0 to 100", content.ThreatIntro);
        Assert.Contains("(1-20)", content.ThreatLow);
        Assert.Contains("(21-40)", content.ThreatMedium);
        Assert.Contains("(41-60)", content.ThreatHigh);
        Assert.Contains("(61-80)", content.ThreatVeryHigh);
        Assert.Contains("(81-100)", content.ThreatExtreme);
        Assert.False(string.IsNullOrWhiteSpace(content.ThreatNone));
    }

    [Fact]
    public void RelationshipExplanation_RelatesTheNumberToTheBandLabels()
    {
        var content = Create();

        Assert.False(string.IsNullOrWhiteSpace(content.RelationshipIntro));
        Assert.StartsWith("Low (1-25)", content.RelationshipLow);
        Assert.StartsWith("Medium (26-75)", content.RelationshipMedium);
        Assert.StartsWith("High (76-100)", content.RelationshipHigh);
    }

    [Fact]
    public void GroupExplanation_DescribesDirectAndChain()
    {
        var content = Create();

        Assert.False(string.IsNullOrWhiteSpace(content.GroupIntro));
        Assert.StartsWith("Direct", content.GroupDirect);
        Assert.StartsWith("Chain", content.GroupChain);
    }

    [Fact]
    public void ShortcutGroups_AreWindowFiltersGeneralInThatOrder()
    {
        var titles = Create().ShortcutGroups.Select(group => group.Title).ToArray();

        Assert.Equal(new[] { UiText.ShortcutsGroupWindow, UiText.ShortcutsGroupFilters, UiText.ShortcutsGroupGeneral }, titles);
        Assert.Equal(new[] { "Window", "Filters", "General" }, titles);
    }

    [Fact]
    public void ShortcutGroups_HoldTheExpectedEntries()
    {
        var groups = Create().ShortcutGroups.ToDictionary(group => group.Title, group => group.Entries.Select(entry => entry.Key).ToArray());

        Assert.Equal(new[] { "Alt + drag", "Win+Down" }, groups["Window"]);
        Assert.Equal(new[] { "Ctrl+R", "Ctrl+right-click", "Ctrl+left-click" }, groups["Filters"]);

        var holdEntry = Create().ShortcutGroups.Single(group => group.Title == UiText.ShortcutsGroupFilters).Entries.Single(entry => entry.Key == UiText.ShortcutsKeyPilotHold);
        Assert.Equal(UiText.ShortcutsActionPilotHold, holdEntry.Action);
        Assert.Equal("Hold pilot (pilot row)", holdEntry.Action);
        Assert.Equal(new[] { "Ctrl+M", "Ctrl+Q" }, groups["General"]);
    }

    [Fact]
    public void ShortcutGroups_OmitTheF1AndDeveloperEntries()
    {
        var entries = Create().ShortcutGroups.SelectMany(group => group.Entries).ToArray();

        Assert.DoesNotContain(entries, entry => entry.Key == "F1");
        Assert.DoesNotContain(entries, entry => entry.Key == "Ctrl+Shift+D");
        Assert.DoesNotContain(entries, entry => entry.Action.Contains("Developer", StringComparison.OrdinalIgnoreCase));
        Assert.All(entries, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Action)));
    }

    [Fact]
    public void AboutTab_ShowsTheVersionString()
    {
        Assert.Equal("KillRight version 0.5.0", Create(version: "0.5.0").AboutVersion);
    }

    [Fact]
    public void AppVersion_StripsBuildMetadataFromTheInformationalVersion()
    {
        var version = AppVersion.Read(typeof(F1HelpContentTests).Assembly);

        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.DoesNotContain('+', version);
    }

    [Fact]
    public void F1ContentSource_HoldsNoTextLiteralsOutsideTheResourceSet()
    {
        var source = File.ReadAllText(FindUiFile("Shortcuts", "F1HelpContent.cs"));
        var literals = Regex.Matches(source, "\"[^\"]*\"").Select(match => match.Value);

        Assert.Empty(literals);
    }

    private static string FindUiFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "KillRight.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);

        return Path.Combine(new[] { directory!.FullName, "Killright.UI" }.Concat(relativeParts).ToArray());
    }
}
