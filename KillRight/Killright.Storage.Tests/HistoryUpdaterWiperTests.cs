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
}
