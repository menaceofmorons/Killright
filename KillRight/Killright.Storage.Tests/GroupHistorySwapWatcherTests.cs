using System.IO;
using System.Text.Json;
using DuckDB.NET.Data;
using Killright.Storage.GroupHistory;
using Xunit;

namespace Killright.Storage.Tests;

// Section 6.4 Agent Test Independence: every test below builds its own
// uniquely named temp paths (sentinel, live status, candidate/replacement
// database, and -- for the Failed/Archive-folder tests -- an isolated
// historic database root, always passed explicitly so archiving logic never
// touches the real %LOCALAPPDATA%\KillRight tree) and cleans up everything
// it created before returning. No test depends on another having run first,
// and every test is safe to run alone, in any order, or more than once.
public sealed class GroupHistorySwapWatcherTests
{
    private static string CreateValidDuckDbFile(string? directory = null, string? fileName = null)
    {
        var path = directory is null
            ? Path.Combine(Path.GetTempPath(), $"KillRight.History.{Guid.NewGuid():N}.duckdb")
            : Path.Combine(directory, fileName ?? $"KillRight.History.{Guid.NewGuid():N}.duckdb");

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

        // Step 19.00.60 REV-B: an isolated root is now always passed so the
        // post-repoint archiving scan never touches the real %LOCALAPPDATA%
        // tree, even though this particular test does not exercise
        // archiving itself (the isolated root's Live folder is never
        // created, so the scan's directory-exists guard returns
        // immediately).
        var replacementFile = CreateValidDuckDbFile();
        var sentinelPath = Path.Combine(Path.GetTempPath(), $"database.new.{Guid.NewGuid():N}");
        File.WriteAllText(sentinelPath, string.Empty);
        var liveStatusPath = Path.Combine(Path.GetTempPath(), $"groupHistory.status.{Guid.NewGuid():N}.json");
        WriteLiveStatus(liveStatusPath, replacementFile);
        var isolatedRoot = Path.Combine(Path.GetTempPath(), $"historyupdater-swap-root.{Guid.NewGuid():N}");

        var watcher = new GroupHistorySwapWatcher(resolver, sentinelPath, liveStatusPath, isolatedRoot);

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
            if (Directory.Exists(isolatedRoot))
                Directory.Delete(isolatedRoot, recursive: true);
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
    public void Watcher_SentinelPresent_CorruptCandidate_FallsBackAndMovesCandidateToFailed_ArchiveUntouched()
    {
        var defaultPath = Path.Combine(Path.GetTempPath(), "KillRight.GroupHistory.duckdb");
        var resolver = new GroupHistoryActiveDatabasePathResolver(defaultPath);

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

            // A failed swap never archives anything.
            var archiveDirectory = Path.Combine(isolatedRoot, HistoryUpdaterStagingPaths.ArchiveDirectoryName);
            Assert.False(Directory.Exists(archiveDirectory));
        }
        finally
        {
            File.Delete(liveStatusPath);
            Directory.Delete(isolatedRoot, recursive: true);
        }
    }

    [Fact]
    public void Watcher_SentinelPresent_ValidCandidate_NoOtherLiveFiles_ArchiveStaysEmpty()
    {
        var defaultPath = Path.Combine(Path.GetTempPath(), "KillRight.GroupHistory.duckdb");
        var resolver = new GroupHistoryActiveDatabasePathResolver(defaultPath);

        var isolatedRoot = Path.Combine(Path.GetTempPath(), $"historyupdater-swap-root.{Guid.NewGuid():N}");
        var liveDirectory = Path.Combine(isolatedRoot, HistoryUpdaterStagingPaths.LiveDirectoryName);
        Directory.CreateDirectory(liveDirectory);

        var candidatePath = CreateValidDuckDbFile(liveDirectory);

        var sentinelPath = Path.Combine(Path.GetTempPath(), $"database.new.{Guid.NewGuid():N}");
        File.WriteAllText(sentinelPath, string.Empty);
        var liveStatusPath = Path.Combine(Path.GetTempPath(), $"groupHistory.status.{Guid.NewGuid():N}.json");
        WriteLiveStatus(liveStatusPath, candidatePath);

        var watcher = new GroupHistorySwapWatcher(resolver, sentinelPath, liveStatusPath, isolatedRoot);

        try
        {
            var result = watcher.CheckAndApply();

            Assert.Equal(GroupHistorySwapResult.Applied, result);

            var archiveDirectory = Path.Combine(isolatedRoot, HistoryUpdaterStagingPaths.ArchiveDirectoryName);
            Assert.False(Directory.Exists(archiveDirectory));
        }
        finally
        {
            File.Delete(liveStatusPath);
            Directory.Delete(isolatedRoot, recursive: true);
        }
    }

    [Fact]
    public void Watcher_SentinelPresent_ValidCandidate_ArchivesSupersededLiveFile_EvenWithFreshlyConstructedResolver()
    {
        // The key regression test for the REV-B fix: the resolver is
        // constructed with a default path that has nothing to do with the
        // real superseded file sitting in Live -- simulating a freshly
        // relaunched application that has no in-memory record of the
        // earlier swap. Archiving must still find and archive the
        // superseded file correctly, because it is derived from Live's own
        // contents, not from the resolver.
        var unrelatedDefaultPath = Path.Combine(Path.GetTempPath(), $"unrelated-default-{Guid.NewGuid():N}.duckdb");
        var resolver = new GroupHistoryActiveDatabasePathResolver(unrelatedDefaultPath);

        var isolatedRoot = Path.Combine(Path.GetTempPath(), $"historyupdater-swap-root.{Guid.NewGuid():N}");
        var liveDirectory = Path.Combine(isolatedRoot, HistoryUpdaterStagingPaths.LiveDirectoryName);
        Directory.CreateDirectory(liveDirectory);

        var supersededPath = CreateValidDuckDbFile(liveDirectory, "KillRight.History.260810.01.duckdb");
        var candidatePath = CreateValidDuckDbFile(liveDirectory, "KillRight.History.260811.01.duckdb");

        var sentinelPath = Path.Combine(Path.GetTempPath(), $"database.new.{Guid.NewGuid():N}");
        File.WriteAllText(sentinelPath, string.Empty);
        var liveStatusPath = Path.Combine(Path.GetTempPath(), $"groupHistory.status.{Guid.NewGuid():N}.json");
        WriteLiveStatus(liveStatusPath, candidatePath);

        var watcher = new GroupHistorySwapWatcher(resolver, sentinelPath, liveStatusPath, isolatedRoot);

        try
        {
            var result = watcher.CheckAndApply();

            Assert.Equal(GroupHistorySwapResult.Applied, result);
            Assert.False(File.Exists(supersededPath));
            Assert.True(File.Exists(candidatePath));

            var archiveDirectory = Path.Combine(isolatedRoot, HistoryUpdaterStagingPaths.ArchiveDirectoryName);
            var archivedFiles = Directory.GetFiles(archiveDirectory);
            Assert.Single(archivedFiles);
            Assert.Equal("KillRight.History.260810.01.duckdb", Path.GetFileName(archivedFiles[0]));
        }
        finally
        {
            File.Delete(liveStatusPath);
            Directory.Delete(isolatedRoot, recursive: true);
        }
    }

    [Fact]
    public void Watcher_SentinelPresent_ValidCandidate_MultipleSupersededLiveFiles_ArchivesOnlyMostRecentAndDeletesOlderOrphans()
    {
        // Reproduces the exact real-world scenario that exposed the REV-A
        // bug: several builds landed in Live with no intervening
        // application launch (Test 5.4's steps 3/4 before this fix).
        var defaultPath = Path.Combine(Path.GetTempPath(), "KillRight.GroupHistory.duckdb");
        var resolver = new GroupHistoryActiveDatabasePathResolver(defaultPath);

        var isolatedRoot = Path.Combine(Path.GetTempPath(), $"historyupdater-swap-root.{Guid.NewGuid():N}");
        var liveDirectory = Path.Combine(isolatedRoot, HistoryUpdaterStagingPaths.LiveDirectoryName);
        Directory.CreateDirectory(liveDirectory);

        var oldestOrphan = CreateValidDuckDbFile(liveDirectory, "KillRight.History.260809.01.duckdb");
        var newerOrphan = CreateValidDuckDbFile(liveDirectory, "KillRight.History.260810.01.duckdb");
        var candidatePath = CreateValidDuckDbFile(liveDirectory, "KillRight.History.260811.01.duckdb");

        File.SetLastWriteTimeUtc(oldestOrphan, DateTime.UtcNow.AddMinutes(-20));
        File.SetLastWriteTimeUtc(newerOrphan, DateTime.UtcNow.AddMinutes(-10));

        var sentinelPath = Path.Combine(Path.GetTempPath(), $"database.new.{Guid.NewGuid():N}");
        File.WriteAllText(sentinelPath, string.Empty);
        var liveStatusPath = Path.Combine(Path.GetTempPath(), $"groupHistory.status.{Guid.NewGuid():N}.json");
        WriteLiveStatus(liveStatusPath, candidatePath);

        var watcher = new GroupHistorySwapWatcher(resolver, sentinelPath, liveStatusPath, isolatedRoot);

        try
        {
            var result = watcher.CheckAndApply();

            Assert.Equal(GroupHistorySwapResult.Applied, result);
            Assert.True(File.Exists(candidatePath));

            // The newer orphan is archived; the older one is deleted
            // outright -- neither remains anywhere in Live.
            Assert.False(File.Exists(newerOrphan));
            Assert.False(File.Exists(oldestOrphan));

            var archiveDirectory = Path.Combine(isolatedRoot, HistoryUpdaterStagingPaths.ArchiveDirectoryName);
            var archivedFiles = Directory.GetFiles(archiveDirectory);
            Assert.Single(archivedFiles);
            Assert.Equal("KillRight.History.260810.01.duckdb", Path.GetFileName(archivedFiles[0]));

            var oldestOrphanInArchive = Path.Combine(archiveDirectory, "KillRight.History.260809.01.duckdb");
            Assert.False(File.Exists(oldestOrphanInArchive));
        }
        finally
        {
            File.Delete(liveStatusPath);
            Directory.Delete(isolatedRoot, recursive: true);
        }
    }

    [Fact]
    public void Watcher_SentinelPresent_ValidCandidate_ArchivingReplacesExistingArchiveContent()
    {
        var defaultPath = Path.Combine(Path.GetTempPath(), "KillRight.GroupHistory.duckdb");
        var resolver = new GroupHistoryActiveDatabasePathResolver(defaultPath);

        var isolatedRoot = Path.Combine(Path.GetTempPath(), $"historyupdater-swap-root.{Guid.NewGuid():N}");
        var liveDirectory = Path.Combine(isolatedRoot, HistoryUpdaterStagingPaths.LiveDirectoryName);
        Directory.CreateDirectory(liveDirectory);

        var archiveDirectory = Path.Combine(isolatedRoot, HistoryUpdaterStagingPaths.ArchiveDirectoryName);
        Directory.CreateDirectory(archiveDirectory);
        var staleArchivedFile = Path.Combine(archiveDirectory, "KillRight.History.260701.01.duckdb");
        File.WriteAllText(staleArchivedFile, "fixture stale archived generation");

        var supersededPath = CreateValidDuckDbFile(liveDirectory, "KillRight.History.260810.01.duckdb");
        var candidatePath = CreateValidDuckDbFile(liveDirectory, "KillRight.History.260811.01.duckdb");

        var sentinelPath = Path.Combine(Path.GetTempPath(), $"database.new.{Guid.NewGuid():N}");
        File.WriteAllText(sentinelPath, string.Empty);
        var liveStatusPath = Path.Combine(Path.GetTempPath(), $"groupHistory.status.{Guid.NewGuid():N}.json");
        WriteLiveStatus(liveStatusPath, candidatePath);

        var watcher = new GroupHistorySwapWatcher(resolver, sentinelPath, liveStatusPath, isolatedRoot);

        try
        {
            var result = watcher.CheckAndApply();

            Assert.Equal(GroupHistorySwapResult.Applied, result);
            Assert.False(File.Exists(supersededPath));

            var archivedFiles = Directory.GetFiles(archiveDirectory);
            Assert.Single(archivedFiles);
            Assert.Equal("KillRight.History.260810.01.duckdb", Path.GetFileName(archivedFiles[0]));
            Assert.False(File.Exists(staleArchivedFile));
        }
        finally
        {
            File.Delete(liveStatusPath);
            Directory.Delete(isolatedRoot, recursive: true);
        }
    }
}
