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

        Assert.Equal("Fleet: 14+ Attackers", content.StyleFleet);
        Assert.Equal("Blob: 6-13 Attackers", content.StyleBlob);
        Assert.Equal("Gang: 2-5 Attackers", content.StyleGang);
    }

    [Fact]
    public void StyleSizes_MatchTheDefaultsTheClassifierUses()
    {
        var content = Create();

        Assert.Equal("Fleet: 11+ Attackers", content.StyleFleet);
        Assert.Equal("Blob: 5-10 Attackers", content.StyleBlob);
        Assert.Equal("Gang: 2-4 Attackers", content.StyleGang);
    }

    [Fact]
    public void ThreatExplanation_HasTheBandTextWithNoNumbers()
    {
        var content = Create();

        Assert.Equal("Threat is how dangerous a pilot is likely to be. The band is calculated from kill history, kill to loss ratio, recent activity and security status.", content.ThreatIntro);
        Assert.Equal("None: No kills on record, or only losses or inactivity", content.ThreatNone);
        Assert.Equal("Low: A few kills; or normally victim/inactive with recent kills", content.ThreatLow);
        Assert.Equal("Medium: A real kill record: can be dangerous.", content.ThreatMedium);
        Assert.Equal("High: An established killer with a solid record.", content.ThreatHigh);
        Assert.Equal("Very High: Many kills and few losses, solo or in groups.", content.ThreatVeryHigh);
        Assert.Equal("Extreme: The heaviest kill history; rarely loses a ship.", content.ThreatExtreme);

        var all = new[] { content.ThreatIntro, content.ThreatNone, content.ThreatLow, content.ThreatMedium, content.ThreatHigh, content.ThreatVeryHigh, content.ThreatExtreme };
        Assert.All(all, text => Assert.DoesNotMatch("[0-9]", text));
    }

    [Fact]
    public void RelationshipExplanation_HasTheBandTextAndTheLiteralShownLine()
    {
        var content = Create();

        Assert.Equal("Relationship looks at the number of shared kills and the confidence in those kills being the result of cooperation and not just coincidence.", content.RelationshipIntro);
        Assert.Equal("## {Band Graph}", content.RelationshipShown);
        Assert.Equal("##: the higher the number the more shared kills between the Pilots", content.RelationshipNumber);
        Assert.Equal("Low: Weak connection, kills in mainly large groups", content.RelationshipLow);
        Assert.Equal("Medium: A believable link, evidence of close cooperation", content.RelationshipMedium);
        Assert.Equal("High: Strong connection, they have a real history", content.RelationshipHigh);
    }

    [Fact]
    public void StyleTab_SuffixTextMatches()
    {
        Assert.Equal("Suffix", UiText.ShortcutsSuffixHeading);
        Assert.Equal("- Beginner", UiText.ShortcutsSuffixBeginnerDescription);
        Assert.Equal("- Podder", UiText.ShortcutsSuffixPodderDescription);
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
