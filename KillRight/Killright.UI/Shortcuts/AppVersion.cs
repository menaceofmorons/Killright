using System.Reflection;

namespace Killright.UI.Shortcuts;

public static class AppVersion
{
    public static string Read(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
            return informational.Split('+')[0];

        return assembly.GetName().Version?.ToString(3) ?? string.Empty;
    }
}
