using Killright.UI.Theme;
using Killright.UI.UiState;
using Xunit;

namespace Killright.UI.Tests;

public class ThemeResolverTests
{
    [Theory]
    [InlineData(AppTheme.Light, true, AppTheme.Light)]
    [InlineData(AppTheme.Light, false, AppTheme.Light)]
    [InlineData(AppTheme.Dark, true, AppTheme.Dark)]
    [InlineData(AppTheme.Dark, false, AppTheme.Dark)]
    [InlineData(AppTheme.FollowWindows, true, AppTheme.Light)]
    [InlineData(AppTheme.FollowWindows, false, AppTheme.Dark)]
    public void Resolve_ReturnsExpectedTheme(AppTheme requested, bool osIsLightTheme, AppTheme expected)
    {
        Assert.Equal(expected, ThemeResolver.Resolve(requested, osIsLightTheme));
    }
}
