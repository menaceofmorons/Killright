using System.IO;
using Killright.Storage.GroupHistory;
using Xunit;

namespace Killright.Storage.Tests;

// Section 6.4 Agent Test Independence: every test below builds its own
// uniquely named root directory (Path.GetTempPath() + a fresh Guid) and
// cleans up everything it created before returning. No test depends on
// another having run first, and every test is safe to run alone, in any
// order, or more than once.
public sealed class HistoryUpdaterFailedBuildStatusTests
{
    private static string CreateTempRootDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"historyupdater-failed-status-root.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    [Fact]
    public void FindFailedBuildFileNames_FailedDirectoryDoesNotExist_ReturnsEmptyWithoutCreatingIt()
    {
        var root = CreateTempRootDirectory();
        var failedDirectory = Path.Combine(root, HistoryUpdaterStagingPaths.FailedDirectoryName);

        var result = HistoryUpdaterFailedBuildStatus.FindFailedBuildFileNames(root);

        Assert.Empty(result);
        Assert.False(Directory.Exists(failedDirectory));

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void FindFailedBuildFileNames_EmptyFailedDirectory_ReturnsEmpty()
    {
        var root = CreateTempRootDirectory();
        var failedDirectory = Path.Combine(root, HistoryUpdaterStagingPaths.FailedDirectoryName);
        Directory.CreateDirectory(failedDirectory);

        var result = HistoryUpdaterFailedBuildStatus.FindFailedBuildFileNames(root);

        Assert.Empty(result);

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void FindFailedBuildFileNames_MatchingFiles_ReturnsNewestFirst()
    {
        var root = CreateTempRootDirectory();
        var failedDirectory = Path.Combine(root, HistoryUpdaterStagingPaths.FailedDirectoryName);
        Directory.CreateDirectory(failedDirectory);

        var olderPath = Path.Combine(failedDirectory, "KillRight.History.260803.01.duckdb");
        var newerPath = Path.Combine(failedDirectory, "KillRight.History.260805.01.duckdb");
        File.WriteAllText(olderPath, "fixture older failed database");
        File.WriteAllText(newerPath, "fixture newer failed database");
        File.SetLastWriteTimeUtc(olderPath, DateTime.UtcNow.AddMinutes(-10));
        File.SetLastWriteTimeUtc(newerPath, DateTime.UtcNow);

        var result = HistoryUpdaterFailedBuildStatus.FindFailedBuildFileNames(root);

        Assert.Equal(new[] { "KillRight.History.260805.01.duckdb", "KillRight.History.260803.01.duckdb" }, result);

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void FindFailedBuildFileNames_NonPromotedNamingFiles_AreExcluded()
    {
        var root = CreateTempRootDirectory();
        var failedDirectory = Path.Combine(root, HistoryUpdaterStagingPaths.FailedDirectoryName);
        Directory.CreateDirectory(failedDirectory);

        // A CORE.-prefixed working-database name and a progress log must
        // never be reported as a failed build file -- only the unprefixed,
        // .duckdb-suffixed promoted naming move_to_failed produces.
        File.WriteAllText(Path.Combine(failedDirectory, "CORE.KillRight.History.260805.01.duckdb"), "fixture");
        File.WriteAllText(Path.Combine(failedDirectory, "KillRight.History.260805.01.progress.log"), "fixture");

        var result = HistoryUpdaterFailedBuildStatus.FindFailedBuildFileNames(root);

        Assert.Empty(result);

        Directory.Delete(root, recursive: true);
    }
}
