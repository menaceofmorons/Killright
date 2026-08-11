using System.IO;
using System.Text.Json;
using DuckDB.NET.Data;
using Killright.Storage.GroupHistory;
using Xunit;

namespace Killright.Storage.Tests;

// Section 6.4 Agent Test Independence: every test below builds its own
// uniquely named temp paths (sentinel, live status, candidate/replacement
// database, and -- for the Failed-folder tests -- an isolated historic
// database root) and cleans up everything it created before returning. No
// test depends on another having run first, and every test is safe to run
// alone, in any order, or more than once. None touch the real
// %LOCALAPPDATA%\KillRight tree.
public sealed class GroupHistorySwapWatcherTests
{
    private static string CreateValidDuckDbFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"KillRight.History.{Guid.NewGuid():N}.duckdb");

        using (var connection = new DuckDBConnection($"Data Source={path}"))
        {
            connection.Open();
        }

        return path;
    }

    private static string CreateCorruptDuckDbFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"KillRight.History.{Guid.NewGuid():N}.duckdb");
        File.WriteAllBytes(path, new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 });
        return path;
    }

    private static void WriteLiveStatus(string liveStatusPath, string activeDatabaseFile)
    {
        var liveStatusDocument = new
        {
            groupHistory = new
            {
                activeDatabaseFile,
                schemaVersion = 1,
                lastCompletedDayUtc = "2026-08-04",
                lastUpdatedUtc = "2026-08-05T18:43:00+00:00",
                updateInProgress = false
            }
        };

        File.WriteAllText(
            liveStatusPath,
            JsonSerializer.Serialize(liveStatusDocument, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    [Fact]
    public void Resolver_DefaultsToLegacyPath_WhenNeverRepointed()
    {
        var defaultPath = Path.Combine(Path.GetTempPath(), "KillRight.GroupHistory.duckdb");

        var resolver = new GroupHistoryActiveDatabasePathResolver(defaultPath);

        Assert.Equal(defaultPath, resolver.GetCurrentDatabasePath());
    }

    [Fact]
    public void Resolver_Repoint_MissingFile_ReturnsCandidateFileMissingAndDoesNotRepoint()
    {
        var defaultPath = Path.Combine(Path.GetTempPath(), "KillRight.GroupHistory.duckdb");
        var resolver = new GroupHistoryActiveDatabasePathResolver(defaultPath);

        var missingFile = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.duckdb");

        var result = resolver.Repoint(missingFile);

        Assert.Equal(GroupHistoryRepointResult.CandidateFileMissing, result);
        Assert.Equal(defaultPath, resolver.GetCurrentDatabasePath());
    }

    [Fact]
    public void Resolver_Repoint_ValidDuckDbFile_ReturnsAppliedAndRepoints()
    {
        var defaultPath = Path.Combine(Path.GetTempPath(), "KillRight.GroupHistory.duckdb");
        var resolver = new GroupHistoryActiveDatabasePathResolver(defaultPath);

        var replacementFile = CreateValidDuckDbFile();

        try
        {
            var result = resolver.Repoint(replacementFile);

            Assert.Equal(GroupHistoryRepointResult.Applied, result);
            Assert.Equal(replacementFile, resolver.GetCurrentDatabasePath());
        }
        finally
        {
            File.Delete(replacementFile);
        }
    }

    [Fact]
    public void Resolver_Repoint_CorruptFile_ReturnsCandidateFileFailedToOpenAndDoesNotRepoint()
    {
        var defaultPath = Path.Combine(Path.GetTempPath(), "KillRight.GroupHistory.duckdb");
        var resolver = new GroupHistoryActiveDatabasePathResolver(defaultPath);

        var corruptFile = CreateCorruptDuckDbFile();

        try
        {
            var result = resolver.Repoint(corruptFile);

            Assert.Equal(GroupHistoryRepointResult.CandidateFileFailedToOpen, result);
            Assert.Equal(defaultPath, resolver.GetCurrentDatabasePath());
        }
        finally
        {
            File.Delete(corruptFile);
        }
    }

    [Fact]
    public void Watcher_NoSentinel_ReturnsNoSentinelAndDoesNotRepoint()
    {
        var defaultPath = Path.Combine(Path.GetTempPath(), "KillRight.GroupHistory.duckdb");
        var resolver = new GroupHistoryActiveDatabasePathResolver(defaultPath);

        var sentinelPath = Path.Combine(Path.GetTempPath(), $"database.new.{Guid.NewGuid():N}");
        var liveStatusPath = Path.Combine(Path.GetTempPath(), $"groupHistory.status.{Guid.NewGuid():N}.json");

        var watcher = new GroupHistorySwapWatcher(resolver, sentinelPath, liveStatusPath);

        var result = watcher.CheckAndApply();

        Assert.Equal(GroupHistorySwapResult.NoSentinel, result);
        Assert.Equal(defaultPath, resolver.GetCurrentDatabasePath());
    }

    [Fact]
    public void Watcher_SentinelPresent_ValidCandidate_DeletesSentinelAndRepointsFromLiveStatus()
    {
        var defaultPath = Path.Combine(Path.GetTempPath(), "KillRight.GroupHistory.duckdb");
        var resolver = new GroupHistoryActiveDatabasePathResolver(defaultPath);

        var replacementFile = CreateValidDuckDbFile();
        var sentinelPath = Path.Combine(Path.GetTempPath(), $"database.new.{Guid.NewGuid():N}");
        File.WriteAllText(sentinelPath, string.Empty);
        var liveStatusPath = Path.Combine(Path.GetTempPath(), $"groupHistory.status.{Guid.NewGuid():N}.json");
        WriteLiveStatus(liveStatusPath, replacementFile);

        var watcher = new GroupHistorySwapWatcher(resolver, sentinelPath, liveStatusPath);

        try
        {
            var result = watcher.CheckAndApply();

            Assert.Equal(GroupHistorySwapResult.Applied, result);
            Assert.False(File.Exists(sentinelPath));
            Assert.Equal(replacementFile, resolver.GetCurrentDatabasePath());
        }
        finally
        {
            File.Delete(replacementFile);
            File.Delete(liveStatusPath);
        }
    }

    [Fact]
    public void Watcher_SentinelPresent_MissingCandidate_DeletesSentinelAndDoesNotRepoint()
    {
        var defaultPath = Path.Combine(Path.GetTempPath(), "KillRight.GroupHistory.duckdb");
        var resolver = new GroupHistoryActiveDatabasePathResolver(defaultPath);

        var missingFile = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.duckdb");
        var sentinelPath = Path.Combine(Path.GetTempPath(), $"database.new.{Guid.NewGuid():N}");
        File.WriteAllText(sentinelPath, string.Empty);
        var liveStatusPath = Path.Combine(Path.GetTempPath(), $"groupHistory.status.{Guid.NewGuid():N}.json");
        WriteLiveStatus(liveStatusPath, missingFile);

        var watcher = new GroupHistorySwapWatcher(resolver, sentinelPath, liveStatusPath);

        try
        {
            var result = watcher.CheckAndApply();

            Assert.Equal(GroupHistorySwapResult.CandidateFileMissing, result);
            Assert.False(File.Exists(sentinelPath));
            Assert.Equal(defaultPath, resolver.GetCurrentDatabasePath());
        }
        finally
        {
            File.Delete(liveStatusPath);
        }
    }

    [Fact]
    public void Watcher_SentinelPresent_CorruptCandidate_FallsBackAndMovesCandidateToFailed()
    {
        var defaultPath = Path.Combine(Path.GetTempPath(), "KillRight.GroupHistory.duckdb");
        var resolver = new GroupHistoryActiveDatabasePathResolver(defaultPath);

        // An isolated historic-database root, matching the
        // rootDirectoryOverride convention HistoryUpdaterWiper/
        // HistoryUpdaterFailedBuildStatus already use, so this test never
        // touches the real %LOCALAPPDATA% tree.
        var isolatedRoot = Path.Combine(Path.GetTempPath(), $"historyupdater-swap-root.{Guid.NewGuid():N}");
        var liveDirectory = Path.Combine(isolatedRoot, HistoryUpdaterStagingPaths.LiveDirectoryName);
        Directory.CreateDirectory(liveDirectory);

        var candidateFileName = $"KillRight.History.{Guid.NewGuid():N}.duckdb";
        var candidatePath = Path.Combine(liveDirectory, candidateFileName);
        File.WriteAllBytes(candidatePath, new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 });

        var sentinelPath = Path.Combine(Path.GetTempPath(), $"database.new.{Guid.NewGuid():N}");
        File.WriteAllText(sentinelPath, string.Empty);
        var liveStatusPath = Path.Combine(Path.GetTempPath(), $"groupHistory.status.{Guid.NewGuid():N}.json");
        WriteLiveStatus(liveStatusPath, candidatePath);

        var watcher = new GroupHistorySwapWatcher(resolver, sentinelPath, liveStatusPath, isolatedRoot);

        try
        {
            var result = watcher.CheckAndApply();

            Assert.Equal(GroupHistorySwapResult.FailedToOpenFallenBackToPrevious, result);
            Assert.Equal(defaultPath, resolver.GetCurrentDatabasePath());
            Assert.False(File.Exists(candidatePath));

            var failedPath = Path.Combine(isolatedRoot, HistoryUpdaterStagingPaths.FailedDirectoryName, candidateFileName);
            Assert.True(File.Exists(failedPath));
        }
        finally
        {
            File.Delete(liveStatusPath);
            Directory.Delete(isolatedRoot, recursive: true);
        }
    }
}
