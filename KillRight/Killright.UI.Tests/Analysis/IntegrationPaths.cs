using System.Text.RegularExpressions;
using Xunit;

namespace Killright.UI.Tests.Analysis;

internal static class IntegrationPaths
{
#if DEBUG
    private const string Profile = "debug";
    private const string Configuration = "Debug";
#else
    private const string Profile = "release";
    private const string Configuration = "Release";
#endif

    public static string EngineDllPath()
    {
        var cargoTargetDir = Environment.GetEnvironmentVariable("CARGO_TARGET_DIR");
        var targetDir = string.IsNullOrWhiteSpace(cargoTargetDir)
            ? Path.Combine(FindAncestorContaining("killright_engine"), "killright_engine", "target")
            : Path.GetFullPath(cargoTargetDir);
        var path = Path.Combine(targetDir, Profile, "killright_engine.dll");

        Assert.True(File.Exists(path), $"killright_engine.dll not found under {targetDir}; build the engine first (cargo build --manifest-path killright_engine/Cargo.toml).");

        return path;
    }

    public static string ShippedSettingsPath()
    {
        return Path.Combine(FindAncestorContaining("KillRight"), "KillRight", "Killright.UI", "config", "settings.json");
    }

    public static string BuiltAppFolder()
    {
        var folder = Path.Combine(FindAncestorContaining("KillRight"), "KillRight", "Killright.UI", "bin", Configuration, "net10.0-windows", "win-x64");

        Assert.True(File.Exists(Path.Combine(folder, "Killright.UI.exe")), $"Killright.UI.exe not found in {folder}; build the solution first.");

        return folder;
    }

    public static string WriteSettingsWithRetryCount(int retryCount)
    {
        var text = File.ReadAllText(ShippedSettingsPath());
        var changed = Regex.Replace(text, "\"retryCount\"\\s*:\\s*\\d+", $"\"retryCount\": {retryCount}");

        Assert.NotEqual(text, changed);

        var path = Path.Combine(Path.GetTempPath(), $"retrySettings.{Guid.NewGuid():N}.json");
        File.WriteAllText(path, changed);

        return path;
    }

    private static string FindAncestorContaining(string folderName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, folderName)))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"No ancestor of {AppContext.BaseDirectory} contains {folderName}.");
    }
}
