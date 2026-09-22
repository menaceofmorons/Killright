using DuckDB.NET.Data;
using Killright.Shared.zKill;
using Killright.Storage.Database;
using Killright.Storage.zKill;
using Xunit;

namespace Killright.Storage.Tests;

public sealed class DuckDbzKillStatisticsCacheTests
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
    public async Task UpsertThenGet_NoHistoryPilot_IsCached()
    {
        var (_, cache) = CreateCache();

        var statistics = new zKillStatistics();

        await cache.UpsertAsync(98798418, statistics, "Unk", noHistory: true);

        var cached = await cache.GetAsync(98798418, TimeSpan.FromDays(30));

        Assert.NotNull(cached);
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
        using var connection = new DuckDBConnection(database.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO main.zkill_statistics_cache (
                character_id, ships_destroyed, solo_kills, solo_ratio, avg_gang_size,
                ships_lost, solo_losses, general_style, checked_at_utc
            ) VALUES (
                {characterId}, 1, 0, 0.0, 0.0, 0, 0, 'Solo', now()
            );
            """;
        command.ExecuteNonQuery();
    }

    private static (KillRightDatabase Database, DuckDbzKillStatisticsCache Cache) CreateCache()
    {
        var path = Path.Combine(Path.GetTempPath(), $"zkillStatistics.{Guid.NewGuid():N}.duckdb");
        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = path });
        database.EnsureCreated();
        return (database, new DuckDbzKillStatisticsCache(database));
    }
}
