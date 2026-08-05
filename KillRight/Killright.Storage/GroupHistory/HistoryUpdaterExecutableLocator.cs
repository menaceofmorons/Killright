namespace Killright.Storage.GroupHistory;

public static class HistoryUpdaterExecutableLocator
{
    public const string ExecutableFileName = "killright_history_updater.exe";
    public const string SolutionFileName = "KillRight.sln";
    public const string CrateDirectoryName = "killright_history_updater";

    public static string? Locate(string? startDirectory = null)
    {
        var solutionDirectory = FindDirectoryContaining(startDirectory ?? AppContext.BaseDirectory, SolutionFileName);

        if (solutionDirectory is null)
            return null;

        var codeDirectory = Directory.GetParent(solutionDirectory)?.FullName;

        if (codeDirectory is null)
            return null;

        var releasePath = Path.Combine(codeDirectory, CrateDirectoryName, "target", "release", ExecutableFileName);

        if (File.Exists(releasePath))
            return releasePath;

        var debugPath = Path.Combine(codeDirectory, CrateDirectoryName, "target", "debug", ExecutableFileName);

        return File.Exists(debugPath) ? debugPath : null;
    }

    private static string? FindDirectoryContaining(string startDirectory, string fileName)
    {
        var directory = new DirectoryInfo(startDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, fileName)))
                return directory.FullName;

            directory = directory.Parent;
        }

        return null;
    }
}
