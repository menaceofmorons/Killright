using System.Text.RegularExpressions;
using Xunit;

namespace Killright.UI.Tests.Documentation;

public sealed class DocumentationLinkTests
{
    private static readonly Regex LinkPattern = new(@"!?\[[^\]]*\]\(([^)\s]+)\)", RegexOptions.Compiled);

    [Fact]
    public void UserGuide_RelativeLinksAndImages_Resolve()
    {
        var guide = GuidePath();
        var missing = RelativeTargets(File.ReadAllText(guide))
            .Where(target => !Exists(Path.GetDirectoryName(guide)!, target))
            .ToList();

        Assert.True(missing.Count == 0, "Unresolved: " + string.Join(", ", missing));
    }

    [Fact]
    public void UserGuide_EveryImageInImagesFolder_IsReferenced()
    {
        var guide = GuidePath();
        var referenced = RelativeTargets(File.ReadAllText(guide))
            .Select(target => Path.GetFileName(StripFragment(target)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unreferenced = Directory.GetFiles(Path.Combine(Path.GetDirectoryName(guide)!, "images"))
            .Select(Path.GetFileName)
            .Where(name => !referenced.Contains(name!))
            .ToList();

        Assert.True(unreferenced.Count == 0, "Unreferenced: " + string.Join(", ", unreferenced));
    }

    [Fact]
    public void UserGuide_HasNoPlaceholderLine()
    {
        var text = File.ReadAllText(GuidePath());

        Assert.DoesNotContain("This section is written separately.", text);
    }

    [Fact]
    public void UserGuide_HasNoDeveloperReference()
    {
        Assert.DoesNotContain("Developer", File.ReadAllText(GuidePath()), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("## The grid")]
    [InlineData("## Pilot Details")]
    [InlineData("## Filters")]
    [InlineData("## Menu")]
    [InlineData("## Shortcuts")]
    public void UserGuide_SectionHasBody(string heading)
    {
        var lines = File.ReadAllLines(GuidePath()).ToList();
        var start = lines.IndexOf(heading);

        Assert.True(start >= 0, heading + " is missing");
        var body = lines.Skip(start + 1)
            .TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal))
            .Where(line => line.Trim().Length > 0 && !line.StartsWith("![", StringComparison.Ordinal));
        Assert.NotEmpty(body);
    }

    [Fact]
    public void Readme_RelativeLinksAndImages_Resolve()
    {
        var readme = ReadmePath();
        var missing = RelativeTargets(File.ReadAllText(readme))
            .Where(target => !Exists(Path.GetDirectoryName(readme)!, target))
            .ToList();

        Assert.True(missing.Count == 0, "Unresolved: " + string.Join(", ", missing));
    }

    [Fact]
    public void Readme_FirstSectionAfterTitle_IsDownload()
    {
        var firstSection = File.ReadAllLines(ReadmePath())
            .First(line => line.StartsWith("## ", StringComparison.Ordinal));

        Assert.Equal("## Download", firstSection);
    }

    [Fact]
    public void Readme_HasNoLocalPath()
    {
        Assert.DoesNotMatch(@"\b[A-Za-z]:[\\/](?![\\/])|\\Users\\|/Users/", File.ReadAllText(ReadmePath()));
    }

    private static IEnumerable<string> RelativeTargets(string markdown) =>
        LinkPattern.Matches(markdown)
            .Select(match => match.Groups[1].Value)
            .Where(target => !target.StartsWith('#') && !Regex.IsMatch(target, @"^[a-zA-Z][a-zA-Z0-9+.-]*:"));

    private static string StripFragment(string target) => target.Split('#')[0];

    private static bool Exists(string baseDirectory, string target)
    {
        var path = Path.GetFullPath(Path.Combine(baseDirectory, Uri.UnescapeDataString(StripFragment(target))));
        return File.Exists(path) || Directory.Exists(path);
    }

    private static string GuidePath() => Path.Combine(RepositoryRoot(), "Docs", "User-Guide.md");

    private static string ReadmePath() => Path.Combine(RepositoryRoot(), "README.md");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Docs", "User-Guide.md")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
