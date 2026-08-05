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
    public void TryCleanUpOrphan_MultipleCandidateFiles_DeletesNothingAndReturnsNull()
    {
        var directory = CreateTempStagingDirectory();
        var firstOrphanPath = Path.Combine(directory, "KillRight.History.260804.01.duckdb");
        var secondOrphanPath = Path.Combine(directory, "KillRight.History.260805.01.duckdb");
        File.WriteAllText(firstOrphanPath, "fixture one");
        File.WriteAllText(secondOrphanPath, "fixture two");

        var statusPath = WriteLiveStatus(string.Empty);

        try
        {
            var result = HistoryUpdaterOrphanedStagingFileCleaner.TryCleanUpOrphan(directory, statusPath);

            Assert.Null(result);
            Assert.True(File.Exists(firstOrphanPath));
            Assert.True(File.Exists(secondOrphanPath));
        }
        finally
        {
            File.Delete(statusPath);
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
