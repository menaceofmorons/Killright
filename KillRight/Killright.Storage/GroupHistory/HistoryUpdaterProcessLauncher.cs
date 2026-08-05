using System.Diagnostics;

namespace Killright.Storage.GroupHistory;

public static class HistoryUpdaterProcessLauncher
{
    public static Process LaunchDetached(string executablePath, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory
        };

        return Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start process: {executablePath} {arguments}");
    }
}
