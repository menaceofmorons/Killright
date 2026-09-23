using DuckDB.NET.Data;
using Killright.Storage.Database;
using Killright.Storage.Killmails;
using Xunit;

namespace Killright.Storage.Tests;

public sealed class DuckDbRecentKillmailCacheTests
{
    private const long ScannedCharacterId = 95465499;
    private const long OtherCharacterId = 91321792;

    [Fact]
    public async Task RemoveExpiredAsync_NonQualifyingKillmailOutsideWindow_IsPurgedWithAttackerRows()
    {
        var (database, cache) = CreateCache(recentWindowDays: 14);

        InsertKillmail(database, 700001, DateTimeOffset.UtcNow.AddDays(-20), isQualifying: false);
        InsertAttacker(database, 700001, ScannedCharacterId);

        await cache.RemoveExpiredAsync();

        Assert.False(KillmailExists(database, 700001));
        Assert.Equal(0, CountAttackers(database, 700001));
    }

    [Fact]
    public async Task RemoveExpiredAsync_QualifyingKillmailOutsideWindow_IsRetainedWithAttackerRows()
    {
        var (database, cache) = CreateCache(recentWindowDays: 14);

        InsertKillmail(database, 700002, DateTimeOffset.UtcNow.AddDays(-20), isQualifying: true);
        InsertAttacker(database, 700002, ScannedCharacterId);
        InsertAttacker(database, 700002, OtherCharacterId);

        await cache.RemoveExpiredAsync();

        Assert.True(KillmailExists(database, 700002));
        Assert.Equal(2, CountAttackers(database, 700002));
    }

    [Fact]
    public async Task RemoveExpiredAsync_NonQualifyingKillmailInsideWindow_IsRetained()
    {
        var (database, cache) = CreateCache(recentWindowDays: 14);

        InsertKillmail(database, 700003, DateTimeOffset.UtcNow.AddDays(-5), isQualifying: false);
        InsertAttacker(database, 700003, ScannedCharacterId);

        await cache.RemoveExpiredAsync();

        Assert.True(KillmailExists(database, 700003));
    }

    [Fact]
    public async Task GetDerivedActivityAsync_CountsKillsAndLossesAcrossAttackerAndVictimRows()
    {
        var (database, cache) = CreateCache(recentWindowDays: 14);

        InsertKillmail(database, 700004, DateTimeOffset.UtcNow.AddDays(-1), isQualifying: true, isSolo: true, victimCharacterId: OtherCharacterId);
        InsertAttacker(database, 700004, ScannedCharacterId);

        InsertKillmail(database, 700005, DateTimeOffset.UtcNow.AddDays(-2), isQualifying: false, isSolo: true, victimCharacterId: ScannedCharacterId);

        var activity = await cache.GetDerivedActivityAsync(ScannedCharacterId);

        Assert.True(activity.HasPublicActivityData);
        Assert.Equal(1, activity.KillsWeek);
        Assert.Equal(1, activity.SoloWeek);
    }

    [Fact]
    public async Task GetDerivedActivityAsync_KillmailOlderThanFixedSevenDayWindow_IsExcludedRegardlessOfRecentWindow()
    {
        var (database, cache) = CreateCache(recentWindowDays: 14);

        InsertKillmail(database, 700006, DateTimeOffset.UtcNow.AddDays(-10), isQualifying: true, victimCharacterId: OtherCharacterId);
        InsertAttacker(database, 700006, ScannedCharacterId);

        var activity = await cache.GetDerivedActivityAsync(ScannedCharacterId);

        Assert.False(activity.HasPublicActivityData);
        Assert.Null(activity.KillsWeek);
    }

    private static (KillRightDatabase Database, DuckDbRecentKillmailCache Cache) CreateCache(int recentWindowDays)
    {
        var path = Path.Combine(Path.GetTempPath(), $"recentKillmailCache.{Guid.NewGuid():N}.duckdb");
        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = path });
        database.EnsureCreated();
        return (database, new DuckDbRecentKillmailCache(database, recentWindowDays));
    }

    private static void InsertKillmail(
        KillRightDatabase database,
        long killmailId,
        DateTimeOffset killTimeUtc,
        bool isQualifying,
        bool isSolo = false,
        long? victimCharacterId = null)
    {
        using var connection = new DuckDBConnection(database.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
                              INSERT INTO main.zkill_killmails (
                                  killmail_id,
                                  killmail_hash,
                                  kill_time_utc,
                                  system_id,
                                  location_id,
                                  victim_character_id,
                                  victim_ship_type_id,
                                  unique_attacker_count,
                                  is_solo,
                                  is_npc,
                                  is_qualifying,
                                  cached_at_utc
                              ) VALUES (
                                  {killmailId},
                                  'hash{killmailId}',
                                  '{killTimeUtc.UtcDateTime:O}',
                                  30000142,
                                  40000001,
                                  {(victimCharacterId?.ToString() ?? "NULL")},
                                  587,
                                  2,
                                  {(isSolo ? "TRUE" : "FALSE")},
                                  FALSE,
                                  {(isQualifying ? "TRUE" : "FALSE")},
                                  '{DateTimeOffset.UtcNow.UtcDateTime:O}'
                              );
                              """;
        command.ExecuteNonQuery();
    }

    private static void InsertAttacker(KillRightDatabase database, long killmailId, long characterId)
    {
        using var connection = new DuckDBConnection(database.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
                              INSERT INTO main.zkill_killmail_attackers (
                                  killmail_id,
                                  character_id,
                                  corporation_id,
                                  alliance_id,
                                  ship_type_id
                              ) VALUES (
                                  {killmailId},
                                  {characterId},
                                  98000001,
                                  NULL,
                                  11567
                              );
                              """;
        command.ExecuteNonQuery();
    }

    private static bool KillmailExists(KillRightDatabase database, long killmailId)
    {
        using var connection = new DuckDBConnection(database.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM main.zkill_killmails WHERE killmail_id = {killmailId};";

        return Convert.ToInt64(command.ExecuteScalar()!) > 0;
    }

    private static int CountAttackers(KillRightDatabase database, long killmailId)
    {
        using var connection = new DuckDBConnection(database.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM main.zkill_killmail_attackers WHERE killmail_id = {killmailId};";

        return Convert.ToInt32(command.ExecuteScalar()!);
    }
}
