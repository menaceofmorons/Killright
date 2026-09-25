using Killright.UI.UiState;

namespace Killright.UI.Theme;

public static class ThemeResolver
{
    public static AppTheme Resolve(AppTheme requested, bool osIsLightTheme)
    {
        return requested switch
        {
            AppTheme.FollowWindows => osIsLightTheme ? AppTheme.Light : AppTheme.Dark,
            _ => requested
        };
    }
}
