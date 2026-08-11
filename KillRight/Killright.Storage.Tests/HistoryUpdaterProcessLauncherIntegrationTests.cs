using System.IO;
using Killright.Storage.GroupHistory;
using Xunit;

namespace Killright.Storage.Tests;

public sealed class HistoryUpdaterProcessLauncherIntegrationTests
{
    [Fact]
    public void LaunchDetached_StartsAProcess_ThatRunsToCompletion()
    {
        var comSpec = Environment.GetEnvironmentVariable("ComSpec");
        Assert.False(string.IsNullOrWhiteSpace(comSpec), "ComSpec environment variable is required to run this test on Windows.");

        var process = HistoryUpdaterProcessLauncher.LaunchDetached(comSpec!, "/c exit 0");

        try
        {
            Assert.True(process.Id > 0);

            var exited = process.WaitForExit(5000);

            Assert.True(exited, "Expected the launched process to exit within 5 seconds.");
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill();
        }
    }

    [Fact]
    public void LaunchDetached_RealHistoryUpdaterExecutable_RunsBuildStagingToCompletionAndUpdatesLiveStatus()
    {
        var executablePath = HistoryUpdaterExecutableLocator.Locate();

        Assert.True(
            executablePath is not null && File.Exists(executablePath),
            "killright_history_updater.exe was not found. Run 'cargo build' in Code/killright_history_updater first (Build Verification 4.1).");

        // Isolates this launch from a developer's real, live interactive
        // staging state (Step CC-07.08.26.02). Without this, the launched
        // process reads and writes the same shared %LOCALAPPDATA%\KillRight
        // tree the Developer window's Pilot testing uses: build-staging
        // --horizon-days 1 copies forward from whatever staging file is
        // already there, so its runtime scales with however much history a
        // developer has accumulated interactively, and a timeout here leaves
        // the shared groupHistory.status.json's updateInProgress flag stuck
        // true (this test's own finally block only kills the process; unlike
        // StopButton_Click, it never calls
        // GroupHistoryLiveStatusResetter.ResetUpdateInProgress()).
        var isolatedRoot = Path.Combine(Path.GetTempPath(), $"killright-history-updater-test.{Guid.NewGuid():N}");
        Directory.CreateDirectory(isolatedRoot);
        var isolatedLiveStatusPath = Path.Combine(isolatedRoot, "KillRight", "config", "groupHistory.status.json");

        Environment.SetEnvironmentVariable("KILLRIGHT_LOCALAPPDATA_OVERRIDE", isolatedRoot);

        var process = HistoryUpdaterProcessLauncher.LaunchDetached(executablePath!, "build-staging --horizon-days 1");

        try
        {
            var exited = process.WaitForExit((int)TimeSpan.FromMinutes(2).TotalMilliseconds);

            Assert.True(exited, "Expected build-staging --horizon-days 1 to complete within 2 minutes.");
            Assert.Equal(0, process.ExitCode);

            var afterStatus = GroupHistoryLiveStatusLoader.LoadOrDefault(isolatedLiveStatusPath);

            Assert.False(afterStatus.UpdateInProgress);

            // Step 19.00.57: the updater's final action for a successful build
            // -- activeDatabaseFile now points at the Live-folder promoted copy
            // (19.00.56 produces the copy; this step advances the live config
            // to it), and lastUpdatedUtc/lastCompletedDayUtc are populated from
            // the Working database's own history_metadata.
            Assert.NotNull(afterStatus.LastUpdatedUtc);
            Assert.NotNull(afterStatus.LastCompletedDayUtc);
            Assert.True(
                File.Exists(afterStatus.ActiveDatabaseFile),
                $"Expected activeDatabaseFile '{afterStatus.ActiveDatabaseFile}' to exist after a successful build-staging run.");

            var liveDirectory = Path.Combine(isolatedRoot, "KillRight", "HistoryUpdater", "Live");

            Assert.Equal(
                Path.GetFullPath(liveDirectory),
                Path.GetFullPath(Path.GetDirectoryName(afterStatus.ActiveDatabaseFile) ?? string.Empty));
            Assert.Matches("^KillRight\\.History\\.", Path.GetFileName(afterStatus.ActiveDatabaseFile));

            var promotedFiles = Directory.Exists(liveDirectory)
                ? Directory.GetFiles(liveDirectory, HistoryUpdaterStagingPaths.PromotedFileSearchPattern)
                : Array.Empty<string>();

            Assert.True(
                promotedFiles.Length == 1,
                $"Expected exactly one promoted file in '{liveDirectory}' after a successful build-staging run, found {promotedFiles.Length}.");
        }
        finally
        {
            if (!process.HasExited)
                process.Kill();

            Environment.SetEnvironmentVariable("KILLRIGHT_LOCALAPPDATA_OVERRIDE", null);

            try
            {
                Directory.Delete(isolatedRoot, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup only -- a lingering handle from the
                // just-killed process must never fail this test's result.
            }
        }
    }
}
