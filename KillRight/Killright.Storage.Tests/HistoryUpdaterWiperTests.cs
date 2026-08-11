using System.IO;
using Killright.Storage.GroupHistory;
using Xunit;

namespace Killright.Storage.Tests;

// Section 6.4 Agent Test Independence: every test below builds its own
// uniquely named root directory (Path.GetTempPath() + a fresh Guid) and its
// own status/signal file paths, and cleans up everything it created in a
// finally-equivalent tail before returning. No test depends on another
// having run first, and every test is safe to run alone, in any order, or
// more than once.
public sealed class HistoryUpdaterWiperTests
{
    private static string CreateTempRootDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"historyupdater-root.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string CreateSubDirectory(string root, string name)
    {
        var directory = Path.Combine(root, name);
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static (string StatusPath, string SignalPath) CreateLiveFiles()
    {
        var statusPath = Path.Combine(Path.GetTempPath(), $"groupHistory.status.{Guid.NewGuid():N}.json");
        var signalPath = Path.Combine(Path.GetTempPath(), $"database.new.{Guid.NewGuid():N}");
        File.WriteAllText(statusPath, "{}");
        File.WriteAllText(signalPath, string.Empty);
        return (statusPath, signalPath);
    }

    [Fact]
    public void WipeAll_DeletesFilesAcrossRootAndAllFourSubfolders_ReturnsCorrectCount()
    {
        var root = CreateTempRootDirectory();
        var working = CreateSubDirectory(root, HistoryUpdaterStagingPaths.WorkingDirectoryName);
        var live = CreateSubDirectory(root, HistoryUpdaterStagingPaths.LiveDirectoryName);
        var archive = CreateSubDirectory(root, HistoryUpdaterStagingPaths.ArchiveDirectoryName);
        var failed = CreateSubDirectory(root, HistoryUpdaterStagingPaths.FailedDirectoryName);

        // One representative file per swept directory, plus a marker in
        // Working (Step 19.00.55: the marker's new home).
        var legacyRootFile = Path.Combine(root, "KillRight.HistoryUpdater.duckdb");
        var workingFile = Path.Combine(working, "CORE.KillRight.History.260810.01.duckdb");
        var markerPath = Path.Combine(working, HistoryUpdaterWiper.LatestValidatedBuildMarkerFileName);
        var liveFile = Path.Combine(live, "KillRight.History.260805.01.duckdb");
        var archiveFile = Path.Combine(archive, "KillRight.History.260804.01.duckdb");
        var failedFile = Path.Combine(failed, "KillRight.History.260803.01.duckdb");

        File.WriteAllText(legacyRootFile, "fixture legacy database");
        File.WriteAllText(workingFile, "fixture working database");
        File.WriteAllText(markerPath, "CORE.KillRight.History.260810.01.duckdb");
        File.WriteAllText(liveFile, "fixture live database");
        File.WriteAllText(archiveFile, "fixture archive database");
        File.WriteAllText(failedFile, "fixture failed database");

        var (statusPath, signalPath) = CreateLiveFiles();

        var deletedCount = HistoryUpdaterWiper.WipeAll(root, statusPath, signalPath);

        Assert.Equal(8, deletedCount);
        Assert.False(File.Exists(legacyRootFile));
        Assert.False(File.Exists(workingFile));
        Assert.False(File.Exists(markerPath));
        Assert.False(File.Exists(liveFile));
        Assert.False(File.Exists(archiveFile));
        Assert.False(File.Exists(failedFile));
        Assert.False(File.Exists(statusPath));
        Assert.False(File.Exists(signalPath));

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void WipeAll_DeletesMarkerOnlyFromWorkingSubfolderNotRoot_ReturnsCorrectCount()
    {
        var root = CreateTempRootDirectory();
        var working = CreateSubDirectory(root, HistoryUpdaterStagingPaths.WorkingDirectoryName);

        // A marker file sitting directly in root (the pre-19.00.55 location)
        // is not the file WipeAll looks for any more -- only the one in
        // Working. Placed here to prove the lookup path genuinely moved, not
        // just that a marker in the right place is still found.
        var staleRootMarkerPath = Path.Combine(root, HistoryUpdaterWiper.LatestValidatedBuildMarkerFileName);
        var workingMarkerPath = Path.Combine(working, HistoryUpdaterWiper.LatestValidatedBuildMarkerFileName);
        File.WriteAllText(staleRootMarkerPath, "not a real marker location any more");
        File.WriteAllText(workingMarkerPath, "CORE.KillRight.History.260810.01.duckdb");

        var (statusPath, signalPath) = CreateLiveFiles();

        var deletedCount = HistoryUpdaterWiper.WipeAll(root, statusPath, signalPath);

        // 1 for the Working marker, 1 for statusPath, 1 for signalPath.
        // staleRootMarkerPath is not a *.duckdb/*.wal/*.progress.log file and
        // is not the marker file name WipeAll checks in root, so it is left
        // in place.
        Assert.Equal(3, deletedCount);
        Assert.False(File.Exists(workingMarkerPath));
        Assert.True(File.Exists(staleRootMarkerPath));

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void WipeAll_NothingToDelete_ReturnsZero()
    {
        var root = CreateTempRootDirectory();
        CreateSubDirectory(root, HistoryUpdaterStagingPaths.WorkingDirectoryName);
        CreateSubDirectory(root, HistoryUpdaterStagingPaths.LiveDirectoryName);
        CreateSubDirectory(root, HistoryUpdaterStagingPaths.ArchiveDirectoryName);
        CreateSubDirectory(root, HistoryUpdaterStagingPaths.FailedDirectoryName);

        var (statusPath, signalPath) = CreateLiveFiles();
        File.Delete(statusPath);
        File.Delete(signalPath);

        var deletedCount = HistoryUpdaterWiper.WipeAll(root, statusPath, signalPath);

        Assert.Equal(0, deletedCount);

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void WipeAll_DeletesLegacyFixedNameDatabaseFileInRoot_ReturnsCorrectCount()
    {
        var root = CreateTempRootDirectory();

        // KillRight.HistoryUpdater.duckdb: the separate fixed-name database
        // used by the standalone create-schema/import-day/rebuild-summary
        // CLI commands (database_path.rs::get_default_database_path). Lives
        // directly under the historic database root, not any subfolder --
        // this guide does not move it. Step 19.00.54 fixed WipeAll not
        // catching it at all; this test confirms the root sweep still
        // catches it after this guide's subfolder changes.
        var legacyDatabaseFile = Path.Combine(root, "KillRight.HistoryUpdater.duckdb");
        File.WriteAllText(legacyDatabaseFile, "fixture legacy database");

        var (statusPath, signalPath) = CreateLiveFiles();
        File.Delete(statusPath);
        File.Delete(signalPath);

        var deletedCount = HistoryUpdaterWiper.WipeAll(root, statusPath, signalPath);

        Assert.Equal(1, deletedCount);
        Assert.False(File.Exists(legacyDatabaseFile));

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void WipeAll_DeletesOrphanedWriteAheadLogFileInWorkingWithNoMatchingDatabaseFile_ReturnsCorrectCount()
    {
        var root = CreateTempRootDirectory();
        var working = CreateSubDirectory(root, HistoryUpdaterStagingPaths.WorkingDirectoryName);

        // A .wal file can outlive the .duckdb file it belongs to (for
        // example if the .duckdb half was already deleted by hand) --
        // WipeAll must still remove it standalone, in the Working subfolder
        // just as it did in the flat directory before this guide.
        var orphanedWalFile = Path.Combine(working, "CORE.KillRight.History.260810.01.duckdb.wal");
        File.WriteAllText(orphanedWalFile, "fixture orphaned wal");

        var (statusPath, signalPath) = CreateLiveFiles();
        File.Delete(statusPath);
        File.Delete(signalPath);

        var deletedCount = HistoryUpdaterWiper.WipeAll(root, statusPath, signalPath);

        Assert.Equal(1, deletedCount);
        Assert.False(File.Exists(orphanedWalFile));

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void FindRemainingArtifacts_BeforeWipe_ListsArtifactsAcrossAllFiveSweptDirectories()
    {
        var root = CreateTempRootDirectory();
        var working = CreateSubDirectory(root, HistoryUpdaterStagingPaths.WorkingDirectoryName);
        var live = CreateSubDirectory(root, HistoryUpdaterStagingPaths.LiveDirectoryName);
        var archive = CreateSubDirectory(root, HistoryUpdaterStagingPaths.ArchiveDirectoryName);
        var failed = CreateSubDirectory(root, HistoryUpdaterStagingPaths.FailedDirectoryName);

        var legacyRootFile = Path.Combine(root, "KillRight.HistoryUpdater.duckdb");
        var workingFile = Path.Combine(working, "CORE.KillRight.History.260810.01.duckdb");
        var markerPath = Path.Combine(working, HistoryUpdaterWiper.LatestValidatedBuildMarkerFileName);
        var liveFile = Path.Combine(live, "KillRight.History.260805.01.duckdb");
        var archiveFile = Path.Combine(archive, "KillRight.History.260804.01.duckdb");
        var failedFile = Path.Combine(failed, "KillRight.History.260803.01.duckdb");

        File.WriteAllText(legacyRootFile, "fixture legacy database");
        File.WriteAllText(workingFile, "fixture working database");
        File.WriteAllText(markerPath, "CORE.KillRight.History.260810.01.duckdb");
        File.WriteAllText(liveFile, "fixture live database");
        File.WriteAllText(archiveFile, "fixture archive database");
        File.WriteAllText(failedFile, "fixture failed database");

        var (statusPath, signalPath) = CreateLiveFiles();

        var remaining = HistoryUpdaterWiper.FindRemainingArtifacts(root, statusPath, signalPath);

        Assert.Equal(8, remaining.Count);
        Assert.Contains(legacyRootFile, remaining);
        Assert.Contains(workingFile, remaining);
        Assert.Contains(markerPath, remaining);
        Assert.Contains(liveFile, remaining);
        Assert.Contains(archiveFile, remaining);
        Assert.Contains(failedFile, remaining);
        Assert.Contains(statusPath, remaining);
        Assert.Contains(signalPath, remaining);

        Directory.Delete(root, recursive: true);
        File.Delete(statusPath);
        File.Delete(signalPath);
    }

    [Fact]
    public void FindRemainingArtifacts_AfterWipeAll_ReturnsEmpty()
    {
        var root = CreateTempRootDirectory();
        var working = CreateSubDirectory(root, HistoryUpdaterStagingPaths.WorkingDirectoryName);
        var live = CreateSubDirectory(root, HistoryUpdaterStagingPaths.LiveDirectoryName);

        File.WriteAllText(Path.Combine(root, "KillRight.HistoryUpdater.duckdb"), "fixture legacy database");
        File.WriteAllText(Path.Combine(working, "CORE.KillRight.History.260810.01.duckdb"), "fixture working database");
        File.WriteAllText(Path.Combine(working, HistoryUpdaterWiper.LatestValidatedBuildMarkerFileName), "marker");
        File.WriteAllText(Path.Combine(live, "KillRight.History.260805.01.duckdb"), "fixture live database");

        var (statusPath, signalPath) = CreateLiveFiles();

        HistoryUpdaterWiper.WipeAll(root, statusPath, signalPath);
        var remaining = HistoryUpdaterWiper.FindRemainingArtifacts(root, statusPath, signalPath);

        Assert.Empty(remaining);

        Directory.Delete(root, recursive: true);
    }
}
