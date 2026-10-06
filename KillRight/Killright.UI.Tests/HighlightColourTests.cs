using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Killright.UI.MenuModal;
using Killright.UI.Theme;
using Killright.UI.UiState;
using Killright.UI.ViewModels;
using Xunit;

namespace Killright.UI.Tests;

public sealed class HighlightColourTests
{
    private static readonly Color Blue = Color.FromRgb(0xAD, 0xD8, 0xE6);

    [Fact]
    public void Palette_HasEightSwatchesWithExpectedLightValues()
    {
        Assert.Equal(8, HighlightSwatchPalette.Swatches.Count);
        Assert.Equal("#FF5B9BD5", HighlightSwatchPalette.Swatches.Single(s => s.Name == "Blue").LightHex);
        Assert.Equal("#FFA77BDB", HighlightSwatchPalette.Swatches.Single(s => s.Name == "Purple").LightHex);
    }

    [Fact]
    public void ResolveHex_PaletteDarkHex_MapsToLightHexInLightThemeAndItselfInDark()
    {
        foreach (var swatch in HighlightSwatchPalette.Swatches)
        {
            Assert.Equal(swatch.LightHex, HighlightColorResolver.ResolveHex(swatch.DarkHex, AppTheme.Light));
            Assert.Equal(swatch.DarkHex, HighlightColorResolver.ResolveHex(swatch.DarkHex, AppTheme.Dark));
        }
    }

    [Fact]
    public void ResolveHex_LowerCasePaletteHex_StillResolves()
    {
        Assert.Equal("#FFA77BDB", HighlightColorResolver.ResolveHex("#ffd8b4fe", AppTheme.Light));
    }

    [Fact]
    public void ResolveHex_NonPaletteHex_UnchangedInBothThemes()
    {
        Assert.Equal("#FF123456", HighlightColorResolver.ResolveHex("#FF123456", AppTheme.Light));
        Assert.Equal("#FF123456", HighlightColorResolver.ResolveHex("#FF123456", AppTheme.Dark));
    }

    [Fact]
    public void TryResolveColor_NullOrInvalid_ReturnsFalse()
    {
        Assert.False(HighlightColorResolver.TryResolveColor(null, AppTheme.Dark, out _));
        Assert.False(HighlightColorResolver.TryResolveColor("not-a-colour", AppTheme.Light, out _));
    }

    [Fact]
    public void ForSameGroup_LightTheme_DarkerThanBase()
    {
        var pilot = HighlightColorCalculator.ForPilot(Blue, 1.0);
        var sameGroup = HighlightColorCalculator.ForSameGroup(Blue, 1.0, AppTheme.Light);

        Assert.True(sameGroup.R + sameGroup.G + sameGroup.B < pilot.R + pilot.G + pilot.B);
    }

    [Fact]
    public void ForSameGroup_DarkTheme_LighterThanBase()
    {
        var pilot = HighlightColorCalculator.ForPilot(Blue, 1.0);
        var sameGroup = HighlightColorCalculator.ForSameGroup(Blue, 1.0, AppTheme.Dark);

        Assert.True(sameGroup.R + sameGroup.G + sameGroup.B > pilot.R + pilot.G + pilot.B);
    }

    [Fact]
    public void ApplyNewPilotColor_LightTheme_UsesLightValueAndDarkUsesDarkValue()
    {
        Sta.Run(() =>
        {
            AppearanceManager.ApplyNewPilotColor("#FFD8B4FE", 0.2, AppTheme.Light);
            var light = Assert.IsType<SolidColorBrush>(Application.Current.Resources[AppearanceManager.NewPilotBrushKey]);
            Assert.Equal(Color.FromArgb(51, 0xA7, 0x7B, 0xDB), light.Color);

            AppearanceManager.ApplyNewPilotColor("#FFD8B4FE", 0.2, AppTheme.Dark);
            var dark = Assert.IsType<SolidColorBrush>(Application.Current.Resources[AppearanceManager.NewPilotBrushKey]);
            Assert.Equal(Color.FromArgb(51, 0xD8, 0xB4, 0xFE), dark.Color);

            AppearanceManager.ApplyNewPilotColor(null, 0.2, AppTheme.Dark);
        });
    }

    [Fact]
    public void Builder_PilotAndRelated_ListEightSwatches_NewPilotListsNineWithDefault()
    {
        Sta.Run(() =>
        {
            var pilot = new WrapPanel();
            var newPilot = new WrapPanel();

            HighlightSwatchPanelBuilder.Build(pilot, "#FFADD8E6", AppTheme.Dark, _ => { });
            HighlightSwatchPanelBuilder.Build(newPilot, null, AppTheme.Dark, _ => { }, () => { });

            Assert.Equal(8, pilot.Children.Count);
            Assert.Equal(9, newPilot.Children.Count);
            Assert.Equal("Default", ((Border)newPilot.Children[0]).ToolTip);
        });
    }

    [Fact]
    public void Builder_Choice_PassesDarkHexInBothThemesAndShowsActiveThemeColour()
    {
        Sta.Run(() =>
        {
            foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light })
            {
                var panel = new WrapPanel();
                string? chosen = null;

                HighlightSwatchPanelBuilder.Build(panel, null, theme, hex => chosen = hex);

                var purple = panel.Children.Cast<Border>().Single(b => (string)b.ToolTip == "Purple");
                var expected = (Color)ColorConverter.ConvertFromString(theme == AppTheme.Light ? "#FFA77BDB" : "#FFD8B4FE")!;

                Assert.Equal(expected, ((SolidColorBrush)purple.Background).Color);

                purple.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = UIElement.MouseLeftButtonDownEvent
                });

                Assert.Equal("#FFD8B4FE", chosen);
            }
        });
    }

    [Fact]
    public void ApplyChip_ShowsActiveThemeValueOrThemeGreyForDefault()
    {
        Sta.Run(() =>
        {
            var chip = new Border();

            HighlightSwatchPanelBuilder.ApplyChip(chip, "#FFD8B4FE", AppTheme.Light);
            Assert.Equal(Color.FromRgb(0xA7, 0x7B, 0xDB), ((SolidColorBrush)chip.Background).Color);

            HighlightSwatchPanelBuilder.ApplyChip(chip, null, AppTheme.Light);
            Assert.Null(chip.Background);
        });
    }

    [Fact]
    public void UiStateDefaults_HighlightColoursUnchanged()
    {
        Assert.Equal("#FFADD8E6", UiStateDefaults.PilotHighlightColorHex);
        Assert.Equal("#FF90EE90", UiStateDefaults.RelatedHighlightColorHex);
        Assert.Null(UiStateDefaults.NewPilotColorHex);
    }
}
