using System.Globalization;
using System.Resources;

namespace Killright.UI.Resources;

internal static class Strings
{
    private static readonly ResourceManager Manager = new("Killright.UI.Resources.Strings", typeof(Strings).Assembly);

    public static string Get(string key) => Get(key, key);

    public static string Get(string key, string fallback) =>
        Manager.GetString(key, CultureInfo.CurrentUICulture) ?? fallback;
}
