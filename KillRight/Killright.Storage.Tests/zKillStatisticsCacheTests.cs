using Microsoft.Data.Sqlite;
using Killright.Shared.zKill;
using Killright.Storage.Database;
using Killright.Storage.zKill;
using Xunit;

namespace Killright.Storage.Tests;

public sealed class zKillStatisticsCacheTests
{
    [Fact]
    public async Task UpsertThenGet_RoundTripsStatisticsFields()
    {
        var (_, cache) = CreateCache();

        var statistics = new zKillStatistics
        {
            shipsDestroyed = 12,
            soloKills = 3,
            soloRatio = 40.0,
            avgGangSize = 4.5,
            shipsLost = 2,
            soloLosses = 1,
            months = new Dictionary<string, zKillStatisticsMonth>
            {
                ["202608"] = new() { Year = 2026, Month = 8, ShipsDestroyed = 5, ShipsLost = 1 }
            }
        };

        await cache.UpsertAsync(95465499, statistics, "Gang", noHistory: false);

        var cached = await cache.GetAsync(95465499, TimeSpan.FromDays(30));

        Assert.NotNull(cached);
        Assert.Equal(12, cached!.shipsDestroyed);
        Assert.Null(cached.months);
    }

    [Fact]
    public async Task UpsertThenGet_RoundTripsPodKills()
    {
        var (_, cache) = CreateCache();

        var statistics = new zKillStatistics
        {
            shipsDestroyed = 12,
            soloKills = 3,
            soloRatio = 40.0,
            avgGangSize = 4.5,
            shipsLost = 2,
            soloLosses = 1,
            podKills = 6,
            podLosses = 4
        };

        await cache.UpsertAsync(95465499, statistics, "Gang", noHistory: false);

        var cached = await cache.GetAsync(95465499, TimeSpan.FromDays(30));

        Assert.NotNull(cached);
        Assert.Equal(6, cached!.podKills);
        Assert.Equal(4, cached.podLosses);
        Assert.Equal(2, cached.shipsLost);
    }

    [Fact]
    public async Task GetAsync_RowWithNullPodLosses_IsTreatedAsExpired()
    {
        var (database, cache) = CreateCache();

        InsertRowWithoutPodLosses(database, 91321792);

        var single = await cache.GetAsync(91321792, TimeSpan.FromDays(30));
        var many = await cache.GetManyAsync([91321792], TimeSpan.FromDays(30));

        Assert.Null(single);
        Assert.Empty(many);
    }

    [Fact]
    public async Task GetAsync_RowWithoutPodKillsColumnValue_TreatsPodKillsAsZero()
    {
        var (database, cache) = CreateCache();

        InsertRowWithoutPodKills(database, 91321792);

        var cached = await cache.GetAsync(91321792, TimeSpan.FromDays(30));

        Assert.NotNull(cached);
        Assert.Equal(0, cached!.podKills);
    }

    [Fact]
    public async Task UpsertThenGet_NoHistoryPilot_IsCached()
    {
        var (_, cache) = CreateCache();

        var statistics = new zKillStatistics();

        await cache.UpsertAsync(98798418, statistics, "Unk", noHistory: true);

        var cached = await cache.GetAsync(98798418, TimeSpan.FromDays(30));

        Assert.NotNull(cached);
        Assert.True(cached!.NoHistory);
    }

    [Fact]
    public async Task ClearNoHistoryMarkerAsync_ClearsPreviouslySetMarker()
    {
        var (_, cache) = CreateCache();

        await cache.UpsertAsync(98798418, new zKillStatistics(), "Unk", noHistory: true);

        await cache.ClearNoHistoryMarkerAsync(98798418);

        var cached = await cache.GetAsync(98798418, TimeSpan.FromDays(30));

        Assert.NotNull(cached);
        Assert.False(cached!.NoHistory);
    }

    [Fact]
    public async Task GetAsync_LegacyRowWithoutMonthsProcessedFlag_IsTreatedAsCacheMiss()
    {
        var (database, cache) = CreateCache();

        InsertLegacyRowWithoutMonthsProcessedFlag(database, 91321792);

        var cached = await cache.GetAsync(91321792, TimeSpan.FromDays(30));

        Assert.Null(cached);
    }

    private static void InsertLegacyRowWithoutMonthsProcessedFlag(KillRightDatabase database, long characterId)
    {
        using var connection = database.OpenConnection();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO main.zkill_statistics_cache (
                character_id, ships_destroyed, solo_kills, solo_ratio, avg_gang_size,
                ships_lost, solo_losses, general_style, checked_at_utc
            ) VALUES (
                {characterId}, 1, 0, 0.0, 0.0, 0, 0, 'Solo', unixepoch()
            );
            """;
        command.ExecuteNonQuery();
    }

    private static void InsertRowWithoutPodKills(KillRightDatabase database, long characterId)
    {
        using var connection = database.OpenConnection();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO main.zkill_statistics_cache (
                character_id, ships_destroyed, solo_kills, solo_ratio, avg_gang_size,
                ships_lost, solo_losses, general_style, months_processed, no_history_marker, checked_at_utc, pod_losses
            ) VALUES (
                {characterId}, 40, 10, 60.0, 3.0, 5, 2, 'Solo', 1, 0, unixepoch(), 0
            );
            """;
        command.ExecuteNonQuery();
    }

    private static void InsertRowWithoutPodLosses(KillRightDatabase database, long characterId)
    {
        using var connection = database.OpenConnection();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO main.zkill_statistics_cache (
                character_id, ships_destroyed, solo_kills, solo_ratio, avg_gang_size,
                ships_lost, solo_losses, general_style, months_processed, no_history_marker, checked_at_utc, pod_kills
            ) VALUES (
                {characterId}, 40, 10, 60.0, 3.0, 5, 2, 'Solo', 1, 0, unixepoch(), 1
            );
            """;
        command.ExecuteNonQuery();
    }

    private static (KillRightDatabase Database, zKillStatisticsCache Cache) CreateCache()
    {
        var path = Path.Combine(Path.GetTempPath(), $"zkillStatistics.{Guid.NewGuid():N}.db");
        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = path });
        database.EnsureCreated();
        return (database, new zKillStatisticsCache(database));
    }
}
