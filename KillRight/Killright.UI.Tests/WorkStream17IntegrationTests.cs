using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Killright.UI.Configuration;
using Killright.UI.Resources;
using Killright.UI.Shortcuts;
using Killright.UI.Theme;
using Killright.UI.UiState;
using Killright.UI.ViewModels;
using Xunit;

namespace Killright.UI.Tests;

[CollectionDefinition(SharedApplicationResourcesCollection.Name, DisableParallelization = true)]
public sealed class SharedApplicationResourcesCollection
{
    public const string Name = "SharedApplicationResources";
}

[Collection(SharedApplicationResourcesCollection.Name)]
public sealed class WorkStream17IntegrationTests
{
    [Theory]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    public void F1Window_OpensAtTheFixedSettingsSize_WithAllSixTabs_FollowingTheTheme(AppTheme theme)
    {
        Sta.Run(() =>
        {
            var previousSettings = App.Settings;
            var previousTheme = AppearanceManager.ResolvedTheme;
            var previousDictionaries = Application.Current.Resources.MergedDictionaries.ToArray();
            ShortcutsWindow? window = null;

            try
            {
                UseRealSettings();
                AppearanceManager.ApplyTheme(theme);

                window = new ShortcutsWindow();

                var settings = App.Settings.Window.F1;

                Assert.Equal(settings.Width, window.Width);
                Assert.Equal(settings.Height, window.Height);
                Assert.Equal(ResizeMode.NoResize, window.ResizeMode);
                Assert.Equal(theme, AppearanceManager.ResolvedTheme);

                var tabs = FindDescendant<TabControl>(window)!.Items.OfType<TabItem>().Select(tab => tab.Header).ToArray();

                Assert.Equal(
                    new object[]
                    {
                        UiText.ShortcutsTabShortcuts,
                        UiText.ShortcutsTabStyle,
                        UiText.ShortcutsTabThreat,
                        UiText.ShortcutsTabRelationship,
                        UiText.ShortcutsTabGroup,
                        UiText.ShortcutsTabAbout,
                    },
                    tabs);

                var content = Assert.IsType<F1HelpContent>(window.DataContext);
                Assert.False(string.IsNullOrWhiteSpace(content.AboutVersion));
                Assert.Contains(content.ShortcutGroups, group => group.Entries.Any());

                var background = Assert.IsType<SolidColorBrush>(window.Background);
                Assert.Equal(ThemeBackground(theme), background.Color);
            }
            finally
            {
                window?.Close();
                AppearanceManager.ApplyTheme(previousTheme);
                SetAppSettings(previousSettings);

                var merged = Application.Current.Resources.MergedDictionaries;
                merged.Clear();

                foreach (var dictionary in previousDictionaries)
                    merged.Add(dictionary);
            }
        });
    }

    [Fact]
    public void BrokenSettingsFile_GivesTheStartupMessageForItsCause_WhileAnOutOfRangeF1SizeDoesNotStopStartup()
    {
        var missing = ApplicationSettingsLoader.Load(Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.json"));

        Assert.NotNull(missing.Failure);
        Assert.Equal(StartupFailureReason.SettingsFileUnreadable, StartupFailureMessage.ReasonFor(missing));

        var malformed = LoadJson("{ not json");

        Assert.NotNull(malformed.Failure);
        Assert.Equal(StartupFailureReason.SettingsInvalid, StartupFailureMessage.ReasonFor(malformed));
        Assert.Contains(UiText.StartupFailureSettingsInvalid, StartupFailureMessage.TextFor(StartupFailureReason.SettingsInvalid));

        var badSize = LoadJson("{ \"window\": { \"f1\": { \"width\": -1, \"height\": 0 } } }");

        Assert.Null(badSize.Failure);
        Assert.Equal(425, badSize.Settings!.Window.F1.Width);
        Assert.Equal(424, badSize.Settings.Window.F1.Height);
    }

    [Fact]
    public void RealSettingsFile_LoadsWithoutFailure_AndDrivesTheF1TextSizes()
    {
        var result = ApplicationSettingsLoader.Load(ApplicationSettingsLoader.GetDefaultSettingsPath());

        Assert.Null(result.Failure);

        var content = F1HelpContent.Create(result.Settings!, "1.2.3");
        var style = result.Settings!.Style;

        Assert.Contains(((int)style.FleetMinimumAverageAttackers).ToString(), content.StyleFleet);
        Assert.Contains("1.2.3", content.AboutVersion);
    }

    [Fact]
    public void Grid_HasBoldHeaders_AGroupColumnThatDoesNotSort_AndTheHoldHandlerOnThePilotCell()
    {
        var xaml = ReadUiFile("MainWindow.xaml");

        Assert.Matches(new Regex("x:Key=\"ColumnHeaderStyle\"[^>]*>(?:(?!</Style>).)*FontWeight\" Value=\"Bold\"", RegexOptions.Singleline), xaml);
        Assert.Matches("ColumnGroup[^>]*CanUserSort=\"False\"", xaml);
        Assert.DoesNotMatch("ColumnGroup[^>]*HeaderStyle=", xaml);
        Assert.Contains("PilotGrid_PreviewMouseLeftButtonDown", xaml);
    }

    [Fact]
    public void PilotHold_TogglesOnAndOffForTheSamePilot_ReleaseAndResetClearIt_AndAPilotWithoutAnIdCannotBeHeld()
    {
        var state = new PilotHoldState();
        var pilot = new PilotReportRow { CharacterId = 1 };
        var unresolved = new PilotReportRow { CharacterId = null };

        Assert.True(state.ToggleHold(pilot));
        Assert.Same(pilot, state.Held);
        Assert.Same(pilot, state.HighlightPilot);

        Assert.False(state.ToggleHold(pilot));
        Assert.Null(state.Held);

        Assert.False(state.ToggleHold(unresolved));
        Assert.Null(state.Held);

        state.ToggleHold(pilot);
        state.ReleaseHold();

        Assert.Null(state.Held);

        state.ToggleHold(pilot);
        state.Hover(unresolved);
        state.SetFiltered(unresolved);
        state.Reset();

        Assert.Null(state.Held);
        Assert.Null(state.Hovered);
        Assert.Null(state.Filtered);
        Assert.Null(state.RelationshipPilot);
    }

    [Fact]
    public void HeldPilot_KeepsItsStyleAndFactionTooltips_WhileAnotherPilotIsHovered()
    {
        var state = new PilotHoldState();
        var held = new PilotReportRow { CharacterId = 1 };
        var other = new PilotReportRow { CharacterId = 2 };

        state.ToggleHold(held);
        state.Hover(other);

        Assert.Same(held, state.RelationshipPilot);
        Assert.Equal("Fleet / Solo", PilotGridTooltips.Style("Fleet", "Solo"));
        Assert.Null(PilotGridTooltips.FactionName(null, _ => "x"));

        state.Reset();

        Assert.Null(state.Held);
    }

    [Fact]
    public void MenuModalAndF1Window_BothMergeTheChromeStylesAndTakeTheirColoursFromTheThemeBrushes()
    {
        foreach (var file in new[] { "MenuModal/MenuModalWindow.xaml", "Shortcuts/ShortcutsWindow.xaml" })
        {
            var xaml = ReadUiFile(file);

            Assert.Contains("Theme/ChromeStyles.xaml", xaml);
            Assert.Contains("Background=\"{DynamicResource Brush.Window.Background}\"", xaml);
            Assert.Contains("Foreground=\"{DynamicResource Brush.Text.Primary}\"", xaml);
        }
    }

    private static Color ThemeBackground(AppTheme theme) =>
        (Color)ColorConverter.ConvertFromString(theme == AppTheme.Light ? "#FFF5F5F5" : "#FF1E1E1E")!;

    private static void UseRealSettings()
    {
        SetAppSettings(ApplicationSettingsLoader.Load(ApplicationSettingsLoader.GetDefaultSettingsPath()).Settings!);
    }

    private static void SetAppSettings(ApplicationSettings? settings) =>
        typeof(App).GetProperty(nameof(App.Settings), BindingFlags.Public | BindingFlags.Static)!.GetSetMethod(nonPublic: true)!.Invoke(null, new object?[] { settings });

    private static SettingsLoadResult LoadJson(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"killright-ws17-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);

        try
        {
            return ApplicationSettingsLoader.Load(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        if (root is T match)
            return match;

        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (FindDescendant<T>(child) is { } found)
                return found;
        }

        return null;
    }

    private static string ReadUiFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "Killright.UI", relativePath);

            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            directory = directory.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
