using System.Diagnostics;
using Killright.Storage.Diagnostics;
using Xunit;

namespace Killright.UI.Tests.Analysis;

public sealed class SettingsStartupStopIntegrationTests
{
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(60);

    [Fact]
    public void BuiltApplication_WithTheSettingsFileRemoved_LogsAndExitsWithoutOpeningAWindow()
    {
        var logged = RunBuiltApplication(appFolder => File.Delete(Path.Combine(appFolder, "config", "settings.json")));

        Assert.Contains("Startup refused", logged);
        Assert.Contains("not found", logged);
    }

    [Fact]
    public void BuiltApplication_WithRetryCountBelowOne_LogsTheKeyAndExitsWithoutOpeningAWindow()
    {
        var logged = RunBuiltApplication(appFolder =>
        {
            var invalid = IntegrationPaths.WriteSettingsWithRetryCount(0);
            File.Move(invalid, Path.Combine(appFolder, "config", "settings.json"), overwrite: true);
        });

        Assert.Contains("Startup refused", logged);
        Assert.Contains("network.zkill.retryCount", logged);
    }

    private static string RunBuiltApplication(Action<string> arrange)
    {
        var appFolder = Path.Combine(Path.GetTempPath(), $"killrightApp.{Guid.NewGuid():N}");
        CopyDirectory(IntegrationPaths.BuiltAppFolder(), appFolder);

        try
        {
            arrange(appFolder);

            var logPath = EngineFailureLogPaths.GetDefaultLogPath();
            var lengthBefore = File.Exists(logPath) ? new FileInfo(logPath).Length : 0;

            using var process = Process.Start(new ProcessStartInfo(Path.Combine(appFolder, "Killright.UI.exe"))
            {
                WorkingDirectory = appFolder,
                UseShellExecute = false
            })!;

            if (!process.WaitForExit(ExitTimeout))
            {
                process.Kill(entireProcessTree: true);
                Assert.Fail($"The application did not exit within {ExitTimeout.TotalSeconds} seconds.");
            }

            Assert.Equal(0, process.ExitCode);

            using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stream.Seek(lengthBefore, SeekOrigin.Begin);

            return new StreamReader(stream).ReadToEnd();
        }
        finally
        {
            Directory.Delete(appFolder, recursive: true);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));

        foreach (var directory in Directory.GetDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}
