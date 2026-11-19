using System.Text.RegularExpressions;
using Xunit;

namespace Killright.UI.Tests.MenuModal;

public sealed class ConfigTabLayoutSourceTests
{
    private static readonly string[] MovedControls =
    {
        "ThemeComboBox", "FontTierComboBox", "PilotSwatchButton", "RelatedSwatchButton", "NewPilotSwatchButton",
    };

    [Fact]
    public void MenuModal_TabListIsConfigColumnsIgnoreListDeveloper()
    {
        var headers = Regex.Matches(Read("MenuModal/MenuModalWindow.xaml"), "<TabItem[^>]*Header=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "{x:Static resources:UiText.MenuTabConfig}",
                "{x:Static resources:UiText.MenuTabColumns}",
                "{x:Static resources:UiText.MenuTabIgnoreList}",
                "Developer",
            },
            headers);
    }

    [Fact]
    public void MenuModal_HasNoAppearanceTab()
    {
        Assert.DoesNotContain("MenuTabAppearance", Read("MenuModal/MenuModalWindow.xaml"));
    }

    [Fact]
    public void MenuModal_MovedControlsSitInTheConfigTab()
    {
        var xaml = Read("MenuModal/MenuModalWindow.xaml");
        var configTab = xaml[xaml.IndexOf("UiText.MenuTabConfig}", StringComparison.Ordinal)..xaml.IndexOf("UiText.MenuTabColumns}", StringComparison.Ordinal)];

        foreach (var control in MovedControls)
            Assert.Contains($"x:Name=\"{control}\"", configTab);
    }

    [Fact]
    public void MenuModal_ThemeAndFontTierShareARowAndColoursFollowOnTheNext()
    {
        var xaml = Read("MenuModal/MenuModalWindow.xaml");
        var theme = Regex.Match(xaml, "x:Name=\"ThemeComboBox\"[^>]*Width=\"(\\d+)\"");
        var tier = Regex.Match(xaml, "x:Name=\"FontTierComboBox\"[^>]*Width=\"(\\d+)\"");

        Assert.True(theme.Success && tier.Success);
        Assert.InRange(int.Parse(theme.Groups[1].Value), 1, 173);
        Assert.InRange(int.Parse(tier.Groups[1].Value), 1, 173);
        var optionsRow = Row(xaml, "OptionsRow", "ColoursRow");
        var coloursRow = Row(xaml, "ColoursRow", "Default");

        Assert.Contains("x:Name=\"ThemeComboBox\"", optionsRow);
        Assert.Contains("x:Name=\"FontTierComboBox\"", optionsRow);
        Assert.Contains("x:Name=\"PilotSwatchButton\"", coloursRow);
        Assert.Contains("x:Name=\"RelatedSwatchButton\"", coloursRow);
        Assert.Contains("x:Name=\"NewPilotSwatchButton\"", coloursRow);
    }

    [Fact]
    public void MenuModal_MovedControlsKeepPreviewHandlersAndCancelRevertsTheme()
    {
        var xaml = Read("MenuModal/MenuModalWindow.xaml");
        var code = Read("MenuModal/MenuModalWindow.xaml.cs");

        Assert.Contains("SelectionChanged=\"Theme_Changed\"", xaml);
        Assert.Contains("SelectionChanged=\"FontTier_Changed\"", xaml);
        Assert.Contains("AppearanceManager.ApplyTheme(App.UiState.Current.Theme);", code);
        Assert.Contains("AppearanceManager.ApplyFontTier(App.UiState.Current.GridFontTier);", code);
    }

    [Theory]
    [InlineData("MenuModal/MenuModalWindow.xaml")]
    [InlineData("Shortcuts/ShortcutsWindow.xaml")]
    public void SecondaryWindows_TakeTheirColoursFromTheThemeBrushes(string path)
    {
        var window = Regex.Match(Read(path), "<Window\\b[^>]*>", RegexOptions.Singleline).Value;

        Assert.Contains("Background=\"{DynamicResource Brush.Window.Background}\"", window);
        Assert.Contains("Foreground=\"{DynamicResource Brush.Text.Primary}\"", window);
    }

    [Theory]
    [InlineData("MenuModal/MenuModalWindow.xaml")]
    [InlineData("Shortcuts/ShortcutsWindow.xaml")]
    public void SecondaryWindows_MergeTheThemedChromeStyles(string path)
    {
        Assert.Contains("Theme/ChromeStyles.xaml", Read(path));
    }

    [Theory]
    [InlineData("TabControl")]
    [InlineData("TabItem")]
    [InlineData("Button")]
    [InlineData("CheckBox")]
    [InlineData("TextBox")]
    [InlineData("ComboBox")]
    [InlineData("ComboBoxItem")]
    [InlineData("ListBox")]
    [InlineData("ListBoxItem")]
    [InlineData("ScrollBar")]
    [InlineData("DataGrid")]
    [InlineData("DataGridColumnHeader")]
    [InlineData("DataGridRow")]
    [InlineData("DataGridCell")]
    public void ChromeStyles_StyleEveryStockControlFromThemeBrushes(string control)
    {
        var xaml = Read("Theme/ChromeStyles.xaml");
        var start = xaml.IndexOf($"<Style TargetType=\"{control}\">", StringComparison.Ordinal);

        Assert.True(start >= 0, control);
        var end = xaml.IndexOf("</Style>", start, StringComparison.Ordinal);
        Assert.Contains("DynamicResource Brush.", xaml[start..end]);
    }

    [Fact]
    public void ChromeStyles_UseNoHardCodedColours()
    {
        Assert.DoesNotMatch("(Color|Fill|Stroke|Background|Foreground|BorderBrush)=\"#", Read("Theme/ChromeStyles.xaml"));
    }

    private static string Row(string xaml, string name, string nextMarker)
    {
        var start = xaml.IndexOf($"x:Name=\"{name}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, name);
        var end = xaml.IndexOf(nextMarker == "Default" ? "MenuConfigDefaultSectionTitle" : $"x:Name=\"{nextMarker}\"", start, StringComparison.Ordinal);
        Assert.True(end > start, nextMarker);

        return xaml[start..end];
    }

    private static string Read(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "KillRight.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);

        return File.ReadAllText(Path.Combine(directory!.FullName, "Killright.UI", relativePath)).Replace("\r\n", "\n");
    }
}
