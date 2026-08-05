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

        var process = HistoryUpdaterProcessLauncher.LaunchDetached(executablePath!, "build-staging --horizon-days 1");

        try
        {
            var exited = process.WaitForExit((int)TimeSpan.FromMinutes(2).TotalMilliseconds);

            Assert.True(exited, "Expected build-staging --horizon-days 1 to complete within 2 minutes.");
            Assert.Equal(0, process.ExitCode);

            var afterStatus = GroupHistoryLiveStatusLoader.LoadOrDefault();

            Assert.False(afterStatus.UpdateInProgress);
            Assert.NotNull(afterStatus.LastUpdatedUtc);
            Assert.NotNull(afterStatus.LastCompletedDayUtc);
            Assert.True(
                File.Exists(afterStatus.ActiveDatabaseFile),
                $"Expected activeDatabaseFile '{afterStatus.ActiveDatabaseFile}' to exist after a successful build-staging run.");
        }
        finally
        {
            if (!process.HasExited)
                process.Kill();
        }
    }
}
