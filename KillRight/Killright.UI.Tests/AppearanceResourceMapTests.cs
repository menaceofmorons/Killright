using Killright.UI.Theme;
using Killright.UI.UiState;
using Xunit;

namespace Killright.UI.Tests;

public class AppearanceResourceMapTests
{
    [Theory]
    [InlineData(AppTheme.Light, "Brushes.Light.xaml")]
    [InlineData(AppTheme.Dark, "Brushes.Dark.xaml")]
    public void ThemeDictionaryUri_MapsResolvedThemeToFile(AppTheme resolvedTheme, string expectedFileName)
    {
        var uri = AppearanceResourceMap.ThemeDictionaryUri(resolvedTheme);

        Assert.EndsWith(expectedFileName, uri.OriginalString);
    }

    [Fact]
    public void ThemeDictionaryUri_FollowWindows_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AppearanceResourceMap.ThemeDictionaryUri(AppTheme.FollowWindows));
    }

    [Theory]
    [InlineData(GridFontTier.Large, "FontTier.Large.xaml")]
    [InlineData(GridFontTier.Medium, "FontTier.Medium.xaml")]
    [InlineData(GridFontTier.Small, "FontTier.Small.xaml")]
    [InlineData(GridFontTier.Tiny, "FontTier.Tiny.xaml")]
    public void FontTierDictionaryUri_MapsTierToFile(GridFontTier tier, string expectedFileName)
    {
        var uri = AppearanceResourceMap.FontTierDictionaryUri(tier);

        Assert.EndsWith(expectedFileName, uri.OriginalString);
    }
}
