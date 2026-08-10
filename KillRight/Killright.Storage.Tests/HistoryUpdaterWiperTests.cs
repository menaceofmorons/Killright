using System.IO;
using Killright.Storage.GroupHistory;
using Xunit;

namespace Killright.Storage.Tests;

public sealed class HistoryUpdaterWiperTests
{
    [Fact]
    public void WipeAll_DeletesStagingFilesMarkerAndLiveFiles_ReturnsCorrectCount()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"staging.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        var stagingFileOne = Path.Combine(directory, "KillRight.History.260801.01.duckdb");
        var stagingFileTwo = Path.Combine(directory, "KillRight.History.260805.01.duckdb");
        var markerPath = Path.Combine(directory, HistoryUpdaterWiper.LatestValidatedBuildMarkerFileName);
        File.WriteAllText(stagingFileOne, "fixture one");
        File.WriteAllText(stagingFileTwo, "fixture two");
        File.WriteAllText(markerPath, "KillRight.History.260805.01.duckdb");

        var statusPath = Path.Combine(Path.GetTempPath(), $"groupHistory.status.{Guid.NewGuid():N}.json");
        var signalPath = Path.Combine(Path.GetTempPath(), $"database.new.{Guid.NewGuid():N}");
        File.WriteAllText(statusPath, "{}");
        File.WriteAllText(signalPath, string.Empty);

        var deletedCount = HistoryUpdaterWiper.WipeAll(directory, statusPath, signalPath);

        Assert.Equal(5, deletedCount);
        Assert.False(File.Exists(stagingFileOne));
        Assert.False(File.Exists(stagingFileTwo));
        Assert.False(File.Exists(markerPath));
        Assert.False(File.Exists(statusPath));
        Assert.False(File.Exists(signalPath));

        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public void WipeAll_DeletesOrphanedProgressLogFileWithoutItsPairedStagingFile_ReturnsCorrectCount()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"staging.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        var orphanedProgressLog = Path.Combine(directory, "KillRight.History.260807.02.progress.log");
        File.WriteAllText(orphanedProgressLog, "2026-08-07T20:14:00+00:00 START 2017-08-01\n");

        var statusPath = Path.Combine(Path.GetTempPath(), $"groupHistory.status.{Guid.NewGuid():N}.json");
        var signalPath = Path.Combine(Path.GetTempPath(), $"database.new.{Guid.NewGuid():N}");

        var deletedCount = HistoryUpdaterWiper.WipeAll(directory, statusPath, signalPath);

        Assert.Equal(1, deletedCount);
        Assert.False(File.Exists(orphanedProgressLog));

        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public void WipeAll_NothingToDelete_ReturnsZero()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"staging.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        var statusPath = Path.Combine(Path.GetTempPath(), $"groupHistory.status.{Guid.NewGuid():N}.json");
        var signalPath = Path.Combine(Path.GetTempPath(), $"database.new.{Guid.NewGuid():N}");

        var deletedCount = HistoryUpdaterWiper.WipeAll(directory, statusPath, signalPath);

        Assert.Equal(0, deletedCount);

        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public void WipeAll_MissingStagingDirectory_DoesNotThrow()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}");
        var statusPath = Path.Combine(Path.GetTempPath(), $"groupHistory.status.{Guid.NewGuid():N}.json");
        var signalPath = Path.Combine(Path.GetTempPath(), $"database.new.{Guid.NewGuid():N}");
        File.WriteAllText(statusPath, "{}");

        var deletedCount = HistoryUpdaterWiper.WipeAll(directory, statusPath, signalPath);

        Assert.Equal(1, deletedCount);
        Assert.False(File.Exists(statusPath));
    }

    [Fact]
    public void WipeAll_DeletesLegacyFixedNameDatabaseFile_ReturnsCorrectCount()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"staging.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        // KillRight.HistoryUpdater.duckdb: the separate fixed-name database used by
        // the standalone create-schema/import-day/rebuild-summary CLI commands
        // (database_path.rs::get_default_database_path). Never matched the old
        // StagingFileSearchPattern ("KillRight.History.*.duckdb") -- Step 19.00.54
        // fixes exactly this gap, worked around manually during 19.00.53 testing
        // per Implementation-Guides-Tracker.xlsx.
        var legacyDatabaseFile = Path.Combine(directory, "KillRight.HistoryUpdater.duckdb");
        File.WriteAllText(legacyDatabaseFile, "fixture legacy database");

        var statusPath = Path.Combine(Path.GetTempPath(), $"groupHistory.status.{Guid.NewGuid():N}.json");
        var signalPath = Path.Combine(Path.GetTempPath(), $"database.new.{Guid.NewGuid():N}");

        var deletedCount = HistoryUpdaterWiper.WipeAll(directory, statusPath, signalPath);

        Assert.Equal(1, deletedCount);
        Assert.False(File.Exists(legacyDatabaseFile));

        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public void WipeAll_DeletesWriteAheadLogFiles_ReturnsCorrectCount()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"staging.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        // Reproduces the stale-WAL finding recorded against 19.00.53 in
        // Implementation-Guides-Tracker.xlsx: a leftover write-ahead-log file
        // beside KillRight.HistoryUpdater.duckdb caused an INSERT OR REPLACE
        // failure until both were deleted by hand.
        var databaseFile = Path.Combine(directory, "KillRight.HistoryUpdater.duckdb");
        var walFile = Path.Combine(directory, "KillRight.HistoryUpdater.duckdb.wal");
        File.WriteAllText(databaseFile, "fixture legacy database");
        File.WriteAllText(walFile, "fixture stale wal");

        var statusPath = Path.Combine(Path.GetTempPath(), $"groupHistory.status.{Guid.NewGuid():N}.json");
        var signalPath = Path.Combine(Path.GetTempPath(), $"database.new.{Guid.NewGuid():N}");

        var deletedCount = HistoryUpdaterWiper.WipeAll(directory, statusPath, signalPath);

        Assert.Equal(2, deletedCount);
        Assert.False(File.Exists(databaseFile));
        Assert.False(File.Exists(walFile));

        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public void WipeAll_DeletesOrphanedWriteAheadLogFileWithNoMatchingDatabaseFile_ReturnsCorrectCount()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"staging.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        // A .wal file can outlive the .duckdb file it belongs to (for example if
        // the .duckdb half was already deleted by hand, as happened during
        // 19.00.53 testing) -- WipeAll must still remove it standalone.
        var orphanedWalFile = Path.Combine(directory, "KillRight.HistoryUpdater.duckdb.wal");
        File.WriteAllText(orphanedWalFile, "fixture orphaned wal");

        var statusPath = Path.Combine(Path.GetTempPath(), $"groupHistory.status.{Guid.NewGuid():N}.json");
        var signalPath = Path.Combine(Path.GetTempPath(), $"database.new.{Guid.NewGuid():N}");

        var deletedCount = HistoryUpdaterWiper.WipeAll(directory, statusPath, signalPath);

        Assert.Equal(1, deletedCount);
        Assert.False(File.Exists(orphanedWalFile));

        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public void FindRemainingArtifacts_BeforeWipe_ListsEveryArtifactType()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"staging.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        var stagingFile = Path.Combine(directory, "KillRight.History.260801.01.duckdb");
        var legacyDatabaseFile = Path.Combine(directory, "KillRight.HistoryUpdater.duckdb");
        var walFile = Path.Combine(directory, "KillRight.HistoryUpdater.duckdb.wal");
        var progressLog = Path.Combine(directory, "KillRight.History.260801.01.progress.log");
        var markerPath = Path.Combine(directory, HistoryUpdaterWiper.LatestValidatedBuildMarkerFileName);
        File.WriteAllText(stagingFile, "fixture staging");
        File.WriteAllText(legacyDatabaseFile, "fixture legacy database");
        File.WriteAllText(walFile, "fixture wal");
        File.WriteAllText(progressLog, "fixture progress log");
        File.WriteAllText(markerPath, "KillRight.History.260801.01.duckdb");

        var statusPath = Path.Combine(Path.GetTempPath(), $"groupHistory.status.{Guid.NewGuid():N}.json");
        var signalPath = Path.Combine(Path.GetTempPath(), $"database.new.{Guid.NewGuid():N}");
        File.WriteAllText(statusPath, "{}");
        File.WriteAllText(signalPath, string.Empty);

        var remaining = HistoryUpdaterWiper.FindRemainingArtifacts(directory, statusPath, signalPath);

        Assert.Equal(7, remaining.Count);
        Assert.Contains(stagingFile, remaining);
        Assert.Contains(legacyDatabaseFile, remaining);
        Assert.Contains(walFile, remaining);
        Assert.Contains(progressLog, remaining);
        Assert.Contains(markerPath, remaining);
        Assert.Contains(statusPath, remaining);
        Assert.Contains(signalPath, remaining);

        Directory.Delete(directory, recursive: true);
        File.Delete(statusPath);
        File.Delete(signalPath);
    }

    [Fact]
    public void FindRemainingArtifacts_AfterWipeAll_ReturnsEmpty()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"staging.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        File.WriteAllText(Path.Combine(directory, "KillRight.History.260801.01.duckdb"), "fixture staging");
        File.WriteAllText(Path.Combine(directory, "KillRight.HistoryUpdater.duckdb"), "fixture legacy database");
        File.WriteAllText(Path.Combine(directory, "KillRight.HistoryUpdater.duckdb.wal"), "fixture wal");
        File.WriteAllText(Path.Combine(directory, HistoryUpdaterWiper.LatestValidatedBuildMarkerFileName), "marker");

        var statusPath = Path.Combine(Path.GetTempPath(), $"groupHistory.status.{Guid.NewGuid():N}.json");
        var signalPath = Path.Combine(Path.GetTempPath(), $"database.new.{Guid.NewGuid():N}");
        File.WriteAllText(statusPath, "{}");
        File.WriteAllText(signalPath, string.Empty);

        HistoryUpdaterWiper.WipeAll(directory, statusPath, signalPath);
        var remaining = HistoryUpdaterWiper.FindRemainingArtifacts(directory, statusPath, signalPath);

        Assert.Empty(remaining);

        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public void FindRemainingArtifacts_MissingStagingDirectoryAndNoLiveFiles_ReturnsEmpty()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}");
        var statusPath = Path.Combine(Path.GetTempPath(), $"groupHistory.status.{Guid.NewGuid():N}.json");
        var signalPath = Path.Combine(Path.GetTempPath(), $"database.new.{Guid.NewGuid():N}");

        var remaining = HistoryUpdaterWiper.FindRemainingArtifacts(directory, statusPath, signalPath);

        Assert.Empty(remaining);
    }
}
