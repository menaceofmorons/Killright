using System.Diagnostics;
using System.Runtime.InteropServices;
using Killright.Storage.Diagnostics;
using Killright.UI.Configuration;
using Xunit;

namespace Killright.UI.Tests.Analysis;

public sealed class SettingsStartupStopIntegrationTests
{
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(60);

    [Fact]
    public void BuiltApplication_WithTheSettingsFileRemoved_ShowsTheDialogLogsAndExitsOnOk()
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
        var runMarker = $"killrightApp.{Guid.NewGuid():N}";
        var appFolder = Path.Combine(Path.GetTempPath(), runMarker);
        CopyDirectory(IntegrationPaths.BuiltAppFolder(), appFolder);

        try
        {
            arrange(appFolder);

            using var process = Process.Start(new ProcessStartInfo(Path.Combine(appFolder, "Killright.UI.exe"))
            {
                WorkingDirectory = appFolder,
                UseShellExecute = false
            })!;

            var dialogShown = ClickOkOnStartupFailureDialog(process);

            if (!process.WaitForExit(ExitTimeout))
            {
                process.Kill(entireProcessTree: true);
                Assert.Fail($"The application did not exit within {ExitTimeout.TotalSeconds} seconds.");
            }

            Assert.True(dialogShown, "The startup failure dialog was not shown.");
            Assert.Equal(0, process.ExitCode);

            return ReadLinesCarrying(runMarker);
        }
        finally
        {
            Directory.Delete(appFolder, recursive: true);
        }
    }

    private static bool ClickOkOnStartupFailureDialog(Process process)
    {
        var deadline = DateTime.UtcNow + ExitTimeout;

        while (DateTime.UtcNow < deadline && !process.HasExited)
        {
            var dialog = FindDialog(process.Id, StartupFailureMessage.Title);

            if (dialog != IntPtr.Zero)
            {
                var okButton = FindWindowEx(dialog, IntPtr.Zero, "Button", null);
                SendMessage(okButton, BmClick, IntPtr.Zero, IntPtr.Zero);
                return true;
            }

            Thread.Sleep(100);
        }

        return false;
    }

    private static IntPtr FindDialog(int processId, string title)
    {
        var found = IntPtr.Zero;

        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);

            if (owner != (uint)processId || !IsWindowVisible(window))
                return true;

            var text = new System.Text.StringBuilder(256);
            GetWindowText(window, text, text.Capacity);

            if (text.ToString() != title)
                return true;

            found = window;
            return false;
        }, IntPtr.Zero);

        return found;
    }

    private const uint BmClick = 0x00F5;

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, System.Text.StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? title);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    private static string ReadLinesCarrying(string runMarker)
    {
        var logPath = EngineFailureLogPaths.GetDefaultLogPath();

        Assert.True(File.Exists(logPath), $"No failure log at {logPath}.");

        using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        var lines = new List<string>();

        while (reader.ReadLine() is { } line)
        {
            if (line.Contains(runMarker, StringComparison.Ordinal))
                lines.Add(line);
        }

        return string.Join(Environment.NewLine, lines);
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
