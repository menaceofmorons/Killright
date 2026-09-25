using Microsoft.Win32;

namespace Killright.UI.Theme;

public static class OsThemeReader
{
    private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string ValueName = "AppsUseLightTheme";

    public static bool IsLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
            var value = key?.GetValue(ValueName);
            return value is not int intValue || intValue != 0;
        }
        catch
        {
            return true;
        }
    }
}
