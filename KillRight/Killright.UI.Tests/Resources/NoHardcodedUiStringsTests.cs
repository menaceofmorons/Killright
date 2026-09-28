using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Killright.UI.Tests.Resources;

public sealed class NoHardcodedUiStringsTests
{
    private static readonly Regex TextAttribute = new(
        "\\b(Header|Content|Text|Title)=\"(?<value>[^\"]*)\"",
        RegexOptions.Compiled);

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> MigratedXamlFilesWithAllowedLiterals =
        new Dictionary<string, IReadOnlyList<string>>
        {
            ["MainWindow.xaml"] = Array.Empty<string>(),
            [Path.Combine("MenuModal", "MenuModalWindow.xaml")] = new[] { string.Empty, "Developer", "⠿" },
            [Path.Combine("Shortcuts", "ShortcutsWindow.xaml")] = Array.Empty<string>(),
            [Path.Combine("InfoSheet", "InfoSheetWindow.xaml")] = new[] { "Verify: ", "Notes: " },
            [Path.Combine("Sde", "SdeFirstRunLoadWindow.xaml")] = Array.Empty<string>(),
        };

    [Fact]
    public void MigratedXamlFiles_ContainNoNewHardcodedText()
    {
        var uiProjectDirectory = FindKillrightUiProjectDirectory();
        var violations = new List<string>();

        foreach (var (relativePath, allowedLiterals) in MigratedXamlFilesWithAllowedLiterals)
        {
            var fullPath = Path.Combine(uiProjectDirectory, relativePath);
            var xaml = File.ReadAllText(fullPath);

            foreach (Match match in TextAttribute.Matches(xaml))
            {
                var value = match.Groups["value"].Value;

                if (value.StartsWith('{'))
                    continue;

                if (allowedLiterals.Contains(value))
                    continue;

                violations.Add($"{relativePath}: {match.Groups[1].Value}=\"{value}\"");
            }
        }

        Assert.True(violations.Count == 0, "Hardcoded UI text found outside Strings.resx:\n" + string.Join("\n", violations));
    }

    private static string FindKillrightUiProjectDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "KillRight.sln")))
            directory = directory.Parent;

        if (directory is null)
            throw new InvalidOperationException("Could not locate KillRight.sln above " + AppContext.BaseDirectory);

        return Path.Combine(directory.FullName, "Killright.UI");
    }
}
