using System.IO;
using System.Text.Json;
using Killright.Storage.GroupHistory;
using Xunit;

namespace Killright.Storage.Tests;

public sealed class GroupHistorySwapWatcherTests
{
    [Fact]
    public void Resolver_DefaultsToLegacyPath_WhenNeverRepointed()
    {
        var defaultPath = Path.Combine(Path.GetTempPath(), "KillRight.GroupHistory.duckdb");

        var resolver = new GroupHistoryActiveDatabasePathResolver(defaultPath);

        Assert.Equal(defaultPath, resolver.GetCurrentDatabasePath());
    }

    [Fact]
    public void Resolver_Repoint_IgnoresMissingFile()
    {
        var defaultPath = Path.Combine(Path.GetTempPath(), "KillRight.GroupHistory.duckdb");
        var resolver = new GroupHistoryActiveDatabasePathResolver(defaultPath);

        var missingFile = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.duckdb");

        var applied = resolver.Repoint(missingFile);

        Assert.False(applied);
        Assert.Equal(defaultPath, resolver.GetCurrentDatabasePath());
    }

    [Fact]
    public void Resolver_Repoint_AppliesExistingFile()
    {
        var defaultPath = Path.Combine(Path.GetTempPath(), "KillRight.GroupHistory.duckdb");
        var resolver = new GroupHistoryActiveDatabasePathResolver(defaultPath);

        var replacementFile = Path.Combine(Path.GetTempPath(), $"KillRight.History.{Guid.NewGuid():N}.duckdb");
        File.WriteAllText(replacementFile, string.Empty);

        try
        {
            var applied = resolver.Repoint(replacementFile);

            Assert.True(applied);
            Assert.Equal(replacementFile, resolver.GetCurrentDatabasePath());
        }
        finally
        {
            File.Delete(replacementFile);
        }
    }

    [Fact]
    public void Watcher_NoSentinel_ReturnsFalseAndDoesNotRepoint()
    {
        var defaultPath = Path.Combine(Path.GetTempPath(), "KillRight.GroupHistory.duckdb");
        var resolver = new GroupHistoryActiveDatabasePathResolver(defaultPath);

        var sentinelPath = Path.Combine(Path.GetTempPath(), $"database.new.{Guid.NewGuid():N}");
        var liveStatusPath = Path.Combine(Path.GetTempPath(), $"groupHistory.status.{Guid.NewGuid():N}.json");

        var watcher = new GroupHistorySwapWatcher(resolver, sentinelPath, liveStatusPath);

        var applied = watcher.CheckAndApply();

        Assert.False(applied);
        Assert.Equal(defaultPath, resolver.GetCurrentDatabasePath());
    }

    [Fact]
    public void Watcher_SentinelPresent_DeletesSentinelAndRepointsFromLiveStatus()
    {
        var defaultPath = Path.Combine(Path.GetTempPath(), "KillRight.GroupHistory.duckdb");
        var resolver = new GroupHistoryActiveDatabasePathResolver(defaultPath);

        var replacementFile = Path.Combine(Path.GetTempPath(), $"KillRight.History.{Guid.NewGuid():N}.duckdb");
        File.WriteAllText(replacementFile, string.Empty);

        var sentinelPath = Path.Combine(Path.GetTempPath(), $"database.new.{Guid.NewGuid():N}");
        File.WriteAllText(sentinelPath, string.Empty);

        var liveStatusPath = Path.Combine(Path.GetTempPath(), $"groupHistory.status.{Guid.NewGuid():N}.json");

        var liveStatusDocument = new
        {
            groupHistory = new
            {
                activeDatabaseFile = replacementFile,
                schemaVersion = 1,
                lastCompletedDayUtc = "2026-08-04",
                lastUpdatedUtc = "2026-08-05T18:43:00+00:00",
                updateInProgress = false
            }
        };

        File.WriteAllText(
            liveStatusPath,
            JsonSerializer.Serialize(liveStatusDocument, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

        var watcher = new GroupHistorySwapWatcher(resolver, sentinelPath, liveStatusPath);

        try
        {
            var applied = watcher.CheckAndApply();

            Assert.True(applied);
            Assert.False(File.Exists(sentinelPath));
            Assert.Equal(replacementFile, resolver.GetCurrentDatabasePath());
        }
        finally
        {
            File.Delete(replacementFile);
            File.Delete(liveStatusPath);
        }
    }
}
