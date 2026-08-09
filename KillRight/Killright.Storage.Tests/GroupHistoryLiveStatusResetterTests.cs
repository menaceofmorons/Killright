using System.IO;
using Killright.Storage.GroupHistory;
using Xunit;

namespace Killright.Storage.Tests;

public sealed class GroupHistoryLiveStatusResetterTests
{
    [Fact]
    public void ResetUpdateInProgress_MissingFile_DoesNothing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"groupHistory.status.{Guid.NewGuid():N}.json");

        GroupHistoryLiveStatusResetter.ResetUpdateInProgress(path);

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void ResetUpdateInProgress_UpdateInProgressTrue_WritesFalsePreservingOtherFields()
    {
        var path = Path.Combine(Path.GetTempPath(), $"groupHistory.status.{Guid.NewGuid():N}.json");

        var json = """
        {
          "groupHistory": {
            "activeDatabaseFile": "C:/Users/Example/AppData/Local/KillRight/HistoryUpdater/KillRight.History.260805.01.duckdb",
            "schemaVersion": 1,
            "lastCompletedDayUtc": "2026-08-04",
            "lastUpdatedUtc": "2026-08-05T18:43:00+00:00",
            "updateInProgress": true
          }
        }
        """;

        File.WriteAllText(path, json);

        try
        {
            GroupHistoryLiveStatusResetter.ResetUpdateInProgress(path);

            var status = GroupHistoryLiveStatusLoader.LoadOrDefault(path);

            Assert.False(status.UpdateInProgress);
            Assert.Equal(
                "C:/Users/Example/AppData/Local/KillRight/HistoryUpdater/KillRight.History.260805.01.duckdb",
                status.ActiveDatabaseFile);
            Assert.Equal(1, status.SchemaVersion);
            Assert.Equal("2026-08-04", status.LastCompletedDayUtc);
            Assert.Equal("2026-08-05T18:43:00+00:00", status.LastUpdatedUtc);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ResetUpdateInProgress_AlreadyFalse_LeavesFileContentUnchanged()
    {
        var path = Path.Combine(Path.GetTempPath(), $"groupHistory.status.{Guid.NewGuid():N}.json");

        var json = """
        {
          "groupHistory": {
            "activeDatabaseFile": "",
            "schemaVersion": 0,
            "lastCompletedDayUtc": null,
            "lastUpdatedUtc": null,
            "updateInProgress": false
          }
        }
        """;

        File.WriteAllText(path, json);

        try
        {
            var before = File.ReadAllText(path);

            GroupHistoryLiveStatusResetter.ResetUpdateInProgress(path);

            var after = File.ReadAllText(path);

            Assert.Equal(before, after);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ResetIfStale_UpdateInProgressFalse_DoesNothing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"groupHistory.status.{Guid.NewGuid():N}.json");

        var json = """
        {
          "groupHistory": {
            "activeDatabaseFile": "",
            "schemaVersion": 0,
            "lastCompletedDayUtc": null,
            "lastUpdatedUtc": null,
            "updateInProgress": false
          }
        }
        """;

        File.WriteAllText(path, json);

        try
        {
            var before = File.ReadAllText(path);

            GroupHistoryLiveStatusResetter.ResetIfStale(path);

            var after = File.ReadAllText(path);

            Assert.Equal(before, after);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ResetIfStale_UpdateInProgressTrueWithLivePid_LeavesFlagUnchanged()
    {
        var path = Path.Combine(Path.GetTempPath(), $"groupHistory.status.{Guid.NewGuid():N}.json");

        var json = $$"""
        {
          "groupHistory": {
            "activeDatabaseFile": "C:/example/KillRight.History.260805.01.duckdb",
            "schemaVersion": 1,
            "lastCompletedDayUtc": "2026-08-04",
            "lastUpdatedUtc": "2026-08-05T18:43:00+00:00",
            "updateInProgress": true,
            "updateInProgressPid": {{Environment.ProcessId}}
          }
        }
        """;

        File.WriteAllText(path, json);

        try
        {
            GroupHistoryLiveStatusResetter.ResetIfStale(path);

            var status = GroupHistoryLiveStatusLoader.LoadOrDefault(path);

            Assert.True(status.UpdateInProgress);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ResetIfStale_UpdateInProgressTrueWithDeadPid_ResetsFlag()
    {
        var comSpec = Environment.GetEnvironmentVariable("ComSpec");
        Assert.False(string.IsNullOrWhiteSpace(comSpec), "ComSpec environment variable is required to run this test on Windows.");

        var deadProcess = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = comSpec!,
            Arguments = "/c exit 0",
            UseShellExecute = false,
            CreateNoWindow = true
        })!;
        deadProcess.WaitForExit(5000);
        var deadPid = deadProcess.Id;

        var path = Path.Combine(Path.GetTempPath(), $"groupHistory.status.{Guid.NewGuid():N}.json");

        var json = $$"""
        {
          "groupHistory": {
            "activeDatabaseFile": "C:/example/KillRight.History.260805.01.duckdb",
            "schemaVersion": 1,
            "lastCompletedDayUtc": "2026-08-04",
            "lastUpdatedUtc": "2026-08-05T18:43:00+00:00",
            "updateInProgress": true,
            "updateInProgressPid": {{deadPid}}
          }
        }
        """;

        File.WriteAllText(path, json);

        try
        {
            GroupHistoryLiveStatusResetter.ResetIfStale(path);

            var status = GroupHistoryLiveStatusLoader.LoadOrDefault(path);

            Assert.False(status.UpdateInProgress);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ResetIfStale_UpdateInProgressTrueWithNoPidRecorded_LeavesFlagUnchanged()
    {
        var path = Path.Combine(Path.GetTempPath(), $"groupHistory.status.{Guid.NewGuid():N}.json");

        var json = """
        {
          "groupHistory": {
            "activeDatabaseFile": "C:/example/KillRight.History.260805.01.duckdb",
            "schemaVersion": 1,
            "lastCompletedDayUtc": "2026-08-04",
            "lastUpdatedUtc": "2026-08-05T18:43:00+00:00",
            "updateInProgress": true
          }
        }
        """;

        File.WriteAllText(path, json);

        try
        {
            GroupHistoryLiveStatusResetter.ResetIfStale(path);

            var status = GroupHistoryLiveStatusLoader.LoadOrDefault(path);

            Assert.True(status.UpdateInProgress);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
