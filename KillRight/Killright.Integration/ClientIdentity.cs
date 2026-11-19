using System.Reflection;

namespace Killright.Integration;

public static class ClientIdentity
{
    public const string ProjectUrl = "https://github.com/menaceofmorons/Killright";

    public static string UserAgent { get; } = Build(typeof(ClientIdentity).Assembly);

    public static string Build(Assembly assembly)
    {
        var version = assembly.GetName().Version ?? new Version(0, 0, 0);

        return $"KillRight/{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)} (+{ProjectUrl})";
    }
}
