using System.Globalization;
using Killright.UI.Resources;

namespace Killright.UI.ViewModels;

public static class SecurityStatusFormatter
{
    public static string Format(double? value)
    {
        if (value is not { } status || !double.IsFinite(status))
            return UiText.PlaceholderUnk;

        var truncated = Math.Truncate((decimal)status * 10m) / 10m;

        return truncated == 0m ? "0.0" : truncated.ToString("0.0", CultureInfo.InvariantCulture);
    }
}
