using System.IO;
using Killright.Storage.GroupHistory;
using Xunit;

namespace Killright.Storage.Tests;

public sealed class HistoryUpdaterOrphanedStagingFileCleanerTests
{
    private static string CreateTempStagingDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"staging.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string WriteLiveStatus(string activeDatabaseFile)
    {
        var path = Path.Combine(Path.GetTempPath(), $"groupHistory.status.{Guid.NewGuid():N}.json");
        var escapedActiveDatabaseFile = activeDatabaseFile.Replace("\\", "/");
        var json = "{ \"groupHistory\": { "
            + "\"activeDatabaseFile\": \"" + escapedActiveDatabaseFile + "\", "
            + "\"schemaVersion\": 1, "
            + "\"lastCompletedDayUtc\": \"2026-08-04\", "
            + "\"lastUpdatedUtc\": \"2026-08-05T18:43:00+00:00\", "
            + "\"updateInProgress\": false"
            + " } }";
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void TryCleanUpOrphan_StagingDirectoryDoesNotExist_ReturnsNull()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}");
        var statusPath = WriteLiveStatus(string.Empty);

        try
        {
            var result = HistoryUpdaterOrphanedStagingFileCleaner.TryCleanUpOrphan(directory, statusPath);

            Assert.Null(result);
        }
        finally
        {
            File.Delete(statusPath);
        }
    }

    [Fact]
    public void TryCleanUpOrphan_NoCandidateFiles_ReturnsNull()
    {
        var directory = CreateTempStagingDirectory();
        var statusPath = WriteLiveStatus(string.Empty);

        try
        {
            var result = HistoryUpdaterOrphanedStagingFileCleaner.TryCleanUpOrphan(directory, statusPath);

            Assert.Null(result);
        }
        finally
        {
            File.Delete(statusPath);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TryCleanUpOrphan_OneOrphanFile_DeletesItAndReturnsPath()
    {
        var directory = CreateTempStagingDirectory();
        var orphanPath = Path.Combine(directory, "KillRight.History.260805.02.duckdb");
        File.WriteAllText(orphanPath, "not a real database, just a fixture");

        var statusPath = WriteLiveStatus(Path.Combine(directory, "KillRight.History.260805.01.duckdb"));

        try
        {
            var result = HistoryUpdaterOrphanedStagingFileCleaner.TryCleanUpOrphan(directory, statusPath);

            Assert.Equal(orphanPath, result);
            Assert.False(File.Exists(orphanPath));
        }
        finally
        {
            File.Delete(statusPath);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TryCleanUpOrphan_MultipleCandidateFiles_DeletesAllAndReturnsTheMostRecentlyWrittenOne()
    {
        var directory = CreateTempStagingDirectory();
        var olderOrphanPath = Path.Combine(directory, "KillRight.History.260804.01.duckdb");
        var newerOrphanPath = Path.Combine(directory, "KillRight.History.260805.01.duckdb");
        File.WriteAllText(olderOrphanPath, "fixture one");
        File.WriteAllText(newerOrphanPath, "fixture two");
        File.SetLastWriteTimeUtc(olderOrphanPath, DateTime.UtcNow.AddHours(-1));
        File.SetLastWriteTimeUtc(newerOrphanPath, DateTime.UtcNow);

        var statusPath = WriteLiveStatus(string.Empty);

        try
        {
            var result = HistoryUpdaterOrphanedStagingFileCleaner.TryCleanUpOrphan(directory, statusPath);

            Assert.Equal(newerOrphanPath, result);
            Assert.False(File.Exists(olderOrphanPath));
            Assert.False(File.Exists(newerOrphanPath));
        }
        finally
        {
            File.Delete(statusPath);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TryCleanUpOrphan_MultipleCandidates_DeletesStaleProgressLogsButPreservesTheMostRecentOnes()
    {
        var directory = CreateTempStagingDirectory();
        var olderOrphanPath = Path.Combine(directory, "KillRight.History.260804.01.duckdb");
        var newerOrphanPath = Path.Combine(directory, "KillRight.History.260805.01.duckdb");
        var olderProgressLogPath = Path.Combine(directory, "KillRight.History.260804.01.progress.log");
        var newerProgressLogPath = Path.Combine(directory, "KillRight.History.260805.01.progress.log");
        File.WriteAllText(olderOrphanPath, "fixture one");
        File.WriteAllText(newerOrphanPath, "fixture two");
        File.WriteAllText(olderProgressLogPath, "stale, from an unrelated earlier killed run");
        File.WriteAllText(newerProgressLogPath, "relevant to the run this Stop just interrupted");
        File.SetLastWriteTimeUtc(olderOrphanPath, DateTime.UtcNow.AddHours(-1));
        File.SetLastWriteTimeUtc(newerOrphanPath, DateTime.UtcNow);

        var statusPath = WriteLiveStatus(string.Empty);

        try
        {
            var result = HistoryUpdaterOrphanedStagingFileCleaner.TryCleanUpOrphan(directory, statusPath);

            Assert.Equal(newerOrphanPath, result);
            Assert.False(File.Exists(olderProgressLogPath));
            Assert.True(File.Exists(newerProgressLogPath));
        }
        finally
        {
            File.Delete(statusPath);
            File.Delete(newerProgressLogPath);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TryCleanUpOrphan_OnlyCandidateIsTheActiveDatabaseFile_ReturnsNullAndLeavesItInPlace()
    {
        var directory = CreateTempStagingDirectory();
        var activePath = Path.Combine(directory, "KillRight.History.260805.01.duckdb");
        File.WriteAllText(activePath, "the just-succeeded, now-active database");

        var statusPath = WriteLiveStatus(activePath);

        try
        {
            var result = HistoryUpdaterOrphanedStagingFileCleaner.TryCleanUpOrphan(directory, statusPath);

            Assert.Null(result);
            Assert.True(File.Exists(activePath));
        }
        finally
        {
            File.Delete(statusPath);
            Directory.Delete(directory, recursive: true);
        }
    }
}
