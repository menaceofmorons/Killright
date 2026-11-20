using System.Text.RegularExpressions;
using Killright.Integration;
using Killright.Storage.Diagnostics;
using Killright.UI.Configuration;
using Killright.UI.Resources;
using Killright.UI.Shortcuts;
using Xunit;

namespace Killright.UI.Tests.Documentation;

public sealed class DocumentationAgreementTests
{
    private const string RuntimeDownloadUrl = "https://dotnet.microsoft.com/download/dotnet/10.0";

    private static string Root => DocumentationLinkTests.RepositoryRoot();

    private static string Guide => File.ReadAllText(Path.Combine(Root, "Docs", "User-Guide.md"));

    private static string Readme => File.ReadAllText(Path.Combine(Root, "README.md"));

    private static string IssueFile(string name) =>
        File.ReadAllText(Path.Combine(Root, ".github", "ISSUE_TEMPLATE", name));

    [Fact]
    public void Guide_ContentsAnchors_MatchHeadings()
    {
        var text = Guide;
        var headings = text.Split('\n')
            .Where(line => line.StartsWith("## ", StringComparison.Ordinal))
            .Select(line => Slug(line[3..]))
            .ToHashSet();
        var anchors = Regex.Matches(text, @"\]\(#([^)\s]+)\)").Select(match => match.Groups[1].Value).ToList();

        Assert.NotEmpty(anchors);
        Assert.True(
            anchors.All(headings.Contains),
            "Unresolved anchors: " + string.Join(", ", anchors.Where(anchor => !headings.Contains(anchor))));
    }

    [Fact]
    public void Guide_ColumnTable_UsesTheResourceColumnLabels()
    {
        var firstColumn = GridTableRows().Select(row => row[0]).ToList();
        var labels = new[]
        {
            UiText.ColumnsLabelThreat, UiText.ColumnsLabelPilot, UiText.ColumnsLabelRelationship,
            UiText.ColumnsLabelStyle, UiText.ColumnsLabelSec, UiText.ColumnsLabelWeek, UiText.ColumnsLabelGroup,
            UiText.ColumnsLabelCorporation, UiText.ColumnsLabelAlliance, UiText.ColumnsLabelFactionWarfare,
            UiText.ColumnsLabelLastKill
        };

        Assert.Equal(labels.Length, firstColumn.Count);
        foreach (var label in labels)
        {
            Assert.Contains(firstColumn, cell => cell == label || cell.EndsWith(" - " + label, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Guide_ShortcutsTable_MatchesTheF1ShortcutsTab()
    {
        var entries = F1HelpContent.Create(new ApplicationSettings(), "1.0.0")
            .ShortcutGroups.SelectMany(group => group.Entries).ToList();
        var rows = TableRowsUnder("## Shortcuts");

        Assert.Equal(entries.Count, rows.Count);
        foreach (var entry in entries)
        {
            var match = Regex.Match(entry.Action, @"^(?<action>.*?)\s*\((?<scope>[^)]*)\)$");
            var expectedKey = match.Success ? $"{entry.Key} ({match.Groups["scope"].Value})" : entry.Key;
            var expectedAction = match.Success ? match.Groups["action"].Value : entry.Action;

            Assert.Contains(rows, row => row[0] == expectedKey && row[1] == expectedAction);
        }
    }

    [Fact]
    public void Guide_MenuSections_UseTheResourceTabAndControlNames()
    {
        var menu = SectionText("## Menu");
        var names = new[]
        {
            UiText.MenuTabConfig, UiText.MenuTabColumns, UiText.MenuTabIgnoreList,
            UiText.MenuConfigAlwaysOnTop, UiText.MenuAppearanceTheme, UiText.MenuAppearanceHighlightColours,
            UiText.MenuAppearanceGridFontSize, UiText.MenuConfigSkipBackupOnClose,
            UiText.MenuConfigSave, UiText.MenuConfigResetMine, UiText.MenuConfigResetSystem,
            UiText.MenuIgnoreListTypePilot, UiText.MenuIgnoreListTypeCorp, UiText.MenuIgnoreListTypeAlliance
        };

        Assert.Contains("### " + UiText.MenuTabConfig, menu);
        Assert.Contains("### " + UiText.MenuTabColumns, menu);
        Assert.Contains("### " + UiText.MenuTabIgnoreList, menu);
        var missing = names.Where(name => !menu.Contains(name, StringComparison.Ordinal)).ToList();
        Assert.True(missing.Count == 0, "Missing from the Menu section: " + string.Join(", ", missing));
    }

    [Fact]
    public void Guide_FilterMenuEntries_UseTheResourceNames()
    {
        var filters = SectionText("## Filters");

        Assert.Contains(UiText.GridFilterMenuIgnore, filters);
        Assert.Contains(UiText.GridFilterMenuFilterTo, filters);
    }

    [Fact]
    public void Guide_QuotedWindowAndDialogText_MatchesTheResources()
    {
        var text = Guide;
        var startupSuffix = UiText.FormatStartupFailure("x").Replace("x ", string.Empty, StringComparison.Ordinal);
        var quoted = new[]
        {
            UiText.WindowTitleSdeFirstRunLoad, UiText.SdeLoadFailedTitle, UiText.SdeButtonRetry,
            UiText.StartupFailureTitle, startupSuffix
        };

        var missing = quoted.Where(item => !text.Contains(item, StringComparison.Ordinal)).ToList();
        Assert.True(missing.Count == 0, "Missing from the guide: " + string.Join(", ", missing));
    }

    [Fact]
    public void LogFileLocation_MatchesAcrossGuideBugFormAndEngine()
    {
        var location = @"%LOCALAPPDATA%\KillRight\" + EngineFailureLogPaths.FileName;

        Assert.Contains(location, IssueFile("bug_report.yml"));
        Assert.Contains(EngineFailureLogPaths.FileName, Guide);
        Assert.Contains(@"%LOCALAPPDATA%\KillRight", Guide);
    }

    [Fact]
    public void BugForm_PointsToTheVersionOnTheAboutTab()
    {
        Assert.Contains(UiText.ShortcutsTabAbout, IssueFile("bug_report.yml"));
    }

    [Fact]
    public void IssueFormFiles_AreWiredToTheReadmeAndGuide()
    {
        var config = IssueFile("config.yml");

        Assert.Contains("blank_issues_enabled: false", config);
        Assert.Contains(ClientIdentity.ProjectUrl + "/discussions", config);
        Assert.Contains(ClientIdentity.ProjectUrl + "/blob/main/Docs/User-Guide.md", config);
        Assert.Contains("label: Steps to reproduce", IssueFile("bug_report.yml"));
        Assert.Contains("labels: [bug]", IssueFile("bug_report.yml").Replace("\"", string.Empty));
        Assert.Contains("labels: [enhancement]", IssueFile("feature_request.yml").Replace("\"", string.Empty));
    }

    [Fact]
    public void ReadmeHelpLinks_AndGuideIssuesLink_UseTheProjectUrl()
    {
        var readme = Readme;
        var help = readme[readme.IndexOf("## Get help", StringComparison.Ordinal)..];
        var links = Regex.Matches(help, @"\]\((https?://[^)\s]+)\)").Select(match => match.Groups[1].Value).ToList();

        Assert.NotEmpty(links);
        Assert.All(links, link => Assert.StartsWith(ClientIdentity.ProjectUrl + "/", link, StringComparison.Ordinal));
        Assert.Contains(ClientIdentity.ProjectUrl + "/issues/new/choose", links);
        Assert.Contains(ClientIdentity.ProjectUrl + "/releases", readme);
        Assert.Contains(ClientIdentity.ProjectUrl + "/issues", Guide);
    }

    [Fact]
    public void ReadmeAndGuide_StateTheSameRuntimeRequirementAndFirstScanSteps()
    {
        var readme = Readme;
        var guide = Guide;

        Assert.Contains(RuntimeDownloadUrl, readme);
        Assert.Contains(RuntimeDownloadUrl, guide);
        Assert.Contains(".NET 10 Desktop Runtime", readme);
        Assert.Contains(".NET 10 Desktop Runtime", guide);
        Assert.Contains("Ctrl+C", readme);
        Assert.Contains("Ctrl+C", guide);
        Assert.Contains("Local, in an intel channel or in a fleet window", readme);
        Assert.Contains("Local, in an intel channel or in a fleet window", guide);
        Assert.Contains("Docs/User-Guide.md", readme);
    }

    private static string Slug(string heading) =>
        Regex.Replace(heading.Trim().ToLowerInvariant(), @"[^a-z0-9 \-]", string.Empty).Replace(' ', '-');

    private static string SectionText(string heading)
    {
        var lines = File.ReadAllLines(Path.Combine(Root, "Docs", "User-Guide.md")).ToList();
        var start = lines.IndexOf(heading);
        Assert.True(start >= 0, heading + " is missing");
        return string.Join('\n', lines.Skip(start + 1).TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal)));
    }

    private static List<string[]> GridTableRows() => TableRowsUnder("## The grid");

    private static List<string[]> TableRowsUnder(string heading) =>
        SectionText(heading).Split('\n')
            .Where(line => line.StartsWith('|') && !line.StartsWith("| ---", StringComparison.Ordinal))
            .Select(line => line.Trim('|').Split('|').Select(cell => cell.Trim()).ToArray())
            .Skip(1)
            .ToList();
}
