using System.IO;
using Killright.Storage.GroupHistory;
using Xunit;

namespace Killright.Storage.Tests;

public sealed class GroupHistoryLiveStatusLoaderTests
{
    [Fact]
    public void LoadOrDefault_MissingFile_ReturnsDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"groupHistory.status.{Guid.NewGuid():N}.json");

        var status = GroupHistoryLiveStatusLoader.LoadOrDefault(path);

        Assert.Equal(string.Empty, status.ActiveDatabaseFile);
        Assert.Equal(0, status.SchemaVersion);
        Assert.Null(status.LastCompletedDayUtc);
        Assert.Null(status.LastUpdatedUtc);
        Assert.False(status.UpdateInProgress);
    }

    [Fact]
    public void LoadOrDefault_ValidFile_ReturnsParsedFields()
    {
        var path = Path.Combine(Path.GetTempPath(), $"groupHistory.status.{Guid.NewGuid():N}.json");

        var json = """
        {
          "groupHistory": {
            "activeDatabaseFile": "C:/Users/Example/AppData/Local/KillRight/HistoryUpdater/KillRight.History.260805.01.duckdb",
            "schemaVersion": 1,
            "lastCompletedDayUtc": "2026-08-04",
            "lastUpdatedUtc": "2026-08-05T18:43:00+00:00",
            "updateInProgress": false
          }
        }
        """;

        File.WriteAllText(path, json);

        try
        {
            var status = GroupHistoryLiveStatusLoader.LoadOrDefault(path);

            Assert.Equal(
                "C:/Users/Example/AppData/Local/KillRight/HistoryUpdater/KillRight.History.260805.01.duckdb",
                status.ActiveDatabaseFile);
            Assert.Equal(1, status.SchemaVersion);
            Assert.Equal("2026-08-04", status.LastCompletedDayUtc);
            Assert.Equal("2026-08-05T18:43:00+00:00", status.LastUpdatedUtc);
            Assert.False(status.UpdateInProgress);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadOrDefault_MalformedFile_ReturnsDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"groupHistory.status.{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ not valid json");

        try
        {
            var status = GroupHistoryLiveStatusLoader.LoadOrDefault(path);

            Assert.Equal(string.Empty, status.ActiveDatabaseFile);
            Assert.False(status.UpdateInProgress);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
