using DuckDB.NET.Data;
using Killright.Shared.zKill;
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
    public async Task RemoveExpiredAsync_NonQualifyingKillmailIsScannedPilotsOnlyLinkedKillmail_IsRetained()
    {
        var (database, cache) = CreateCache(recentWindowDays: 14);

        InsertActivityCache(database, ScannedCharacterId);
        InsertKillmail(database, 700011, DateTimeOffset.UtcNow.AddDays(-20), isQualifying: false);
        InsertAttacker(database, 700011, ScannedCharacterId);

        await cache.RemoveExpiredAsync();

        Assert.True(KillmailExists(database, 700011));
        Assert.Equal(1, CountAttackers(database, 700011));
    }

    [Fact]
    public async Task RemoveExpiredAsync_NonQualifyingKillmailSupersededByNewerLinkedKillmail_IsPurged()
    {
        var (database, cache) = CreateCache(recentWindowDays: 14);

        InsertActivityCache(database, ScannedCharacterId);
        InsertKillmail(database, 700012, DateTimeOffset.UtcNow.AddDays(-25), isQualifying: false);
        InsertAttacker(database, 700012, ScannedCharacterId);
        InsertKillmail(database, 700013, DateTimeOffset.UtcNow.AddDays(-20), isQualifying: false);
        InsertAttacker(database, 700013, ScannedCharacterId);

        await cache.RemoveExpiredAsync();

        Assert.False(KillmailExists(database, 700012));
        Assert.True(KillmailExists(database, 700013));
    }

    [Fact]
    public async Task RemoveExpiredAsync_NonQualifyingKillmailForNeverScannedPilot_IsPurgedDespiteBeingMostRecent()
    {
        var (database, cache) = CreateCache(recentWindowDays: 14);

        InsertKillmail(database, 700014, DateTimeOffset.UtcNow.AddDays(-20), isQualifying: false);
        InsertAttacker(database, 700014, OtherCharacterId);

        await cache.RemoveExpiredAsync();

        Assert.False(KillmailExists(database, 700014));
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

    [Fact]
    public async Task GetDerivedActivityAsync_PodKillByScannedPilot_IsExcludedFromKillsAndSoloWeek()
    {
        var (database, cache) = CreateCache(recentWindowDays: 14);

        InsertKillmail(database, 700015, DateTimeOffset.UtcNow.AddDays(-1), isQualifying: true, isSolo: true, victimCharacterId: OtherCharacterId, victimShipTypeId: 670);
        InsertAttacker(database, 700015, ScannedCharacterId);

        var activity = await cache.GetDerivedActivityAsync(ScannedCharacterId);

        Assert.True(activity.HasPublicActivityData);
        Assert.Equal(0, activity.KillsWeek);
        Assert.Equal(0, activity.SoloWeek);
    }

    [Fact]
    public async Task GetDerivedActivityAsync_OnlyRecentActivityIsPodKill_LastActiveNotSetFromIt()
    {
        var (database, cache) = CreateCache(recentWindowDays: 14);

        InsertKillmail(database, 700016, DateTimeOffset.UtcNow.AddDays(-1), isQualifying: true, victimCharacterId: OtherCharacterId, victimShipTypeId: 670);
        InsertAttacker(database, 700016, ScannedCharacterId);

        var activity = await cache.GetDerivedActivityAsync(ScannedCharacterId);

        Assert.True(activity.HasPublicActivityData);
        Assert.Null(activity.LastActiveUtc);
        Assert.Null(activity.LastActivityType);
    }

    [Fact]
    public async Task GetDerivedActivityAsync_PodKillMoreRecentThanShipKill_LastActiveComesFromShipKill()
    {
        var (database, cache) = CreateCache(recentWindowDays: 14);

        InsertKillmail(database, 700017, DateTimeOffset.UtcNow.AddDays(-3), isQualifying: true, victimCharacterId: OtherCharacterId, victimShipTypeId: 587);
        InsertAttacker(database, 700017, ScannedCharacterId);

        InsertKillmail(database, 700018, DateTimeOffset.UtcNow.AddDays(-1), isQualifying: true, victimCharacterId: OtherCharacterId, victimShipTypeId: 670);
        InsertAttacker(database, 700018, ScannedCharacterId);

        var activity = await cache.GetDerivedActivityAsync(ScannedCharacterId);

        Assert.Equal(zKillActivityType.Kill, activity.LastActivityType);
        Assert.NotNull(activity.LastActiveUtc);
    }

    [Fact]
    public async Task GetMostRecentKillmailAsync_MostRecentIsPodKillByScannedPilot_SkipsToPriorShipKill()
    {
        var (database, cache) = CreateCache(recentWindowDays: 14);

        InsertKillmail(database, 700019, DateTimeOffset.UtcNow.AddDays(-3), isQualifying: true, victimCharacterId: OtherCharacterId, victimShipTypeId: 587);
        InsertAttacker(database, 700019, ScannedCharacterId);

        InsertKillmail(database, 700020, DateTimeOffset.UtcNow.AddDays(-1), isQualifying: true, victimCharacterId: OtherCharacterId, victimShipTypeId: 670);
        InsertAttacker(database, 700020, ScannedCharacterId);

        var result = await cache.GetMostRecentKillmailAsync(ScannedCharacterId);

        Assert.NotNull(result);
        Assert.Equal(zKillActivityType.Kill, result!.ActivityType);
        Assert.Equal(587, result.VictimShipTypeId);
    }

    [Fact]
    public async Task GetMostRecentKillmailAsync_MostRecentIsALoss_ReturnsLossDetail()
    {
        var (database, cache) = CreateCache(recentWindowDays: 14);

        InsertKillmail(database, 700007, DateTimeOffset.UtcNow.AddDays(-1), isQualifying: true, victimCharacterId: ScannedCharacterId);
        InsertKillmail(database, 700008, DateTimeOffset.UtcNow.AddDays(-3), isQualifying: true, victimCharacterId: OtherCharacterId);
        InsertAttacker(database, 700008, ScannedCharacterId);

        var result = await cache.GetMostRecentKillmailAsync(ScannedCharacterId);

        Assert.NotNull(result);
        Assert.Equal(zKillActivityType.Loss, result!.ActivityType);
        Assert.Equal(587, result.ShipTypeId);
        Assert.Null(result.VictimShipTypeId);
        Assert.Null(result.AttackerCount);
    }

    [Fact]
    public async Task GetMostRecentKillmailAsync_MostRecentIsAKill_ReturnsKillDetail()
    {
        var (database, cache) = CreateCache(recentWindowDays: 14);

        InsertKillmail(database, 700009, DateTimeOffset.UtcNow.AddDays(-3), isQualifying: true, victimCharacterId: ScannedCharacterId);
        InsertKillmail(database, 700010, DateTimeOffset.UtcNow.AddDays(-1), isQualifying: true, victimCharacterId: OtherCharacterId);
        InsertAttacker(database, 700010, ScannedCharacterId);

        var result = await cache.GetMostRecentKillmailAsync(ScannedCharacterId);

        Assert.NotNull(result);
        Assert.Equal(zKillActivityType.Kill, result!.ActivityType);
        Assert.Equal(11567, result.ShipTypeId);
        Assert.Equal(587, result.VictimShipTypeId);
        Assert.Equal(2, result.AttackerCount);
    }

    [Fact]
    public async Task GetMostRecentKillmailAsync_MostRecentIsAKill_ReturnsWeaponTypeId()
    {
        var (database, cache) = CreateCache(recentWindowDays: 14);

        InsertKillmail(database, 700021, DateTimeOffset.UtcNow.AddDays(-1), isQualifying: true, victimCharacterId: OtherCharacterId);
        InsertAttacker(database, 700021, ScannedCharacterId, weaponTypeId: 3074);

        var result = await cache.GetMostRecentKillmailAsync(ScannedCharacterId);

        Assert.NotNull(result);
        Assert.Equal(3074, result!.WeaponTypeId);
    }

    [Fact]
    public async Task GetMostRecentKillmailAsync_MostRecentIsALoss_WeaponTypeIdIsNull()
    {
        var (database, cache) = CreateCache(recentWindowDays: 14);

        InsertKillmail(database, 700022, DateTimeOffset.UtcNow.AddDays(-1), isQualifying: true, victimCharacterId: ScannedCharacterId);

        var result = await cache.GetMostRecentKillmailAsync(ScannedCharacterId);

        Assert.NotNull(result);
        Assert.Null(result!.WeaponTypeId);
    }

    [Fact]
    public async Task GetDerivedActivityAsync_LatestActivityIsLossAfterKill_LastKillUtcIsTheKillTime()
    {
        var (database, cache) = CreateCache(recentWindowDays: 14);
        var killTime = DateTimeOffset.UtcNow.AddDays(-3);

        InsertKillmail(database, 700023, killTime, isQualifying: true, victimCharacterId: OtherCharacterId);
        InsertAttacker(database, 700023, ScannedCharacterId);
        InsertKillmail(database, 700024, DateTimeOffset.UtcNow.AddDays(-1), isQualifying: true, victimCharacterId: ScannedCharacterId);

        var activity = await cache.GetDerivedActivityAsync(ScannedCharacterId);

        Assert.Equal(zKillActivityType.Loss, activity.LastActivityType);
        Assert.NotNull(activity.LastKillUtc);
        Assert.True((activity.LastKillUtc!.Value - killTime).Duration() < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task GetDerivedActivityAsync_OnlyALossExists_LastKillUtcIsNull()
    {
        var (database, cache) = CreateCache(recentWindowDays: 14);

        InsertKillmail(database, 700025, DateTimeOffset.UtcNow.AddDays(-1), isQualifying: true, victimCharacterId: ScannedCharacterId);

        var activity = await cache.GetDerivedActivityAsync(ScannedCharacterId);

        Assert.Equal(zKillActivityType.Loss, activity.LastActivityType);
        Assert.Null(activity.LastKillUtc);
    }

    [Fact]
    public async Task GetDerivedActivityAsync_NewestKillIsPodKill_LastKillUtcSkipsIt()
    {
        var (database, cache) = CreateCache(recentWindowDays: 14);
        var shipKillTime = DateTimeOffset.UtcNow.AddDays(-3);

        InsertKillmail(database, 700026, shipKillTime, isQualifying: true, victimCharacterId: OtherCharacterId, victimShipTypeId: 587);
        InsertAttacker(database, 700026, ScannedCharacterId);
        InsertKillmail(database, 700027, DateTimeOffset.UtcNow.AddDays(-1), isQualifying: true, victimCharacterId: OtherCharacterId, victimShipTypeId: 670);
        InsertAttacker(database, 700027, ScannedCharacterId);

        var activity = await cache.GetDerivedActivityAsync(ScannedCharacterId);

        Assert.NotNull(activity.LastKillUtc);
        Assert.True((activity.LastKillUtc!.Value - shipKillTime).Duration() < TimeSpan.FromSeconds(1));
    }

    [Theory]
    [InlineData(670)]
    [InlineData(33328)]
    public async Task GetMostRecentKillmailAsync_MostRecentIsPodLoss_SkipsToPriorShipKill(long podShipTypeId)
    {
        var (database, cache) = CreateCache(recentWindowDays: 14);

        InsertKillmail(database, 700028, DateTimeOffset.UtcNow.AddDays(-3), isQualifying: true, victimCharacterId: OtherCharacterId, victimShipTypeId: 587);
        InsertAttacker(database, 700028, ScannedCharacterId);
        InsertKillmail(database, 700029, DateTimeOffset.UtcNow.AddDays(-1), isQualifying: true, victimCharacterId: ScannedCharacterId, victimShipTypeId: podShipTypeId);

        var result = await cache.GetMostRecentKillmailAsync(ScannedCharacterId);

        Assert.NotNull(result);
        Assert.Equal(zKillActivityType.Kill, result!.ActivityType);
        Assert.Equal(587, result.VictimShipTypeId);
    }

    [Fact]
    public async Task GetMostRecentKillmailAsync_MostRecentIsPodLossAfterShipLoss_ReturnsShipLoss()
    {
        var (database, cache) = CreateCache(recentWindowDays: 14);

        InsertKillmail(database, 700030, DateTimeOffset.UtcNow.AddDays(-3), isQualifying: true, victimCharacterId: ScannedCharacterId, victimShipTypeId: 11567);
        InsertKillmail(database, 700031, DateTimeOffset.UtcNow.AddDays(-1), isQualifying: true, victimCharacterId: ScannedCharacterId, victimShipTypeId: 670);

        var result = await cache.GetMostRecentKillmailAsync(ScannedCharacterId);

        Assert.NotNull(result);
        Assert.Equal(zKillActivityType.Loss, result!.ActivityType);
        Assert.Equal(11567, result.ShipTypeId);
    }

    [Fact]
    public async Task GetMostRecentKillmailAsync_OnlyPodKillAndPodLoss_ReturnsNull()
    {
        var (database, cache) = CreateCache(recentWindowDays: 14);

        InsertKillmail(database, 700032, DateTimeOffset.UtcNow.AddDays(-2), isQualifying: true, victimCharacterId: OtherCharacterId, victimShipTypeId: 670);
        InsertAttacker(database, 700032, ScannedCharacterId);
        InsertKillmail(database, 700033, DateTimeOffset.UtcNow.AddDays(-1), isQualifying: true, victimCharacterId: ScannedCharacterId, victimShipTypeId: 33328);

        var result = await cache.GetMostRecentKillmailAsync(ScannedCharacterId);

        Assert.Null(result);
    }

    [Fact]
    public async Task RemoveExpiredAsync_NewestKillmailIsPod_OlderNonPodKillmailKeptAsFloor()
    {
        var (database, cache) = CreateCache(recentWindowDays: 14);

        InsertActivityCache(database, ScannedCharacterId);
        InsertKillmail(database, 700034, DateTimeOffset.UtcNow.AddDays(-25), isQualifying: false, victimCharacterId: OtherCharacterId, victimShipTypeId: 587);
        InsertAttacker(database, 700034, ScannedCharacterId);
        InsertKillmail(database, 700035, DateTimeOffset.UtcNow.AddDays(-20), isQualifying: false, victimCharacterId: ScannedCharacterId, victimShipTypeId: 670);

        await cache.RemoveExpiredAsync();

        Assert.True(KillmailExists(database, 700034));
        Assert.False(KillmailExists(database, 700035));
    }

    [Fact]
    public async Task RemoveExpiredAsync_OnlyPodKillmailsOutsideWindow_ArePurged()
    {
        var (database, cache) = CreateCache(recentWindowDays: 14);

        InsertActivityCache(database, ScannedCharacterId);
        InsertKillmail(database, 700036, DateTimeOffset.UtcNow.AddDays(-25), isQualifying: false, victimCharacterId: OtherCharacterId, victimShipTypeId: 670);
        InsertAttacker(database, 700036, ScannedCharacterId);

        await cache.RemoveExpiredAsync();

        Assert.False(KillmailExists(database, 700036));
    }

    [Fact]
    public async Task GetMostRecentKillmailAsync_NoRetainedKillmails_ReturnsNull()
    {
        var (_, cache) = CreateCache(recentWindowDays: 14);

        var result = await cache.GetMostRecentKillmailAsync(ScannedCharacterId);

        Assert.Null(result);
    }

    private static (KillRightDatabase Database, DuckDbRecentKillmailCache Cache) CreateCache(int recentWindowDays)
    {
        var path = Path.Combine(Path.GetTempPath(), $"recentKillmailCache.{Guid.NewGuid():N}.duckdb");
        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = path });
        database.EnsureCreated();
        return (database, new DuckDbRecentKillmailCache(database, recentWindowDays));
    }

    private static void InsertActivityCache(KillRightDatabase database, long characterId)
    {
        using var connection = new DuckDBConnection(database.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
                              INSERT INTO main.zkill_activity_cache (
                                  character_id,
                                  has_public_activity_data,
                                  checked_at_utc
                              ) VALUES (
                                  {characterId},
                                  TRUE,
                                  '{DateTimeOffset.UtcNow.UtcDateTime:O}'
                              );
                              """;
        command.ExecuteNonQuery();
    }

    private static void InsertKillmail(
        KillRightDatabase database,
        long killmailId,
        DateTimeOffset killTimeUtc,
        bool isQualifying,
        bool isSolo = false,
        long? victimCharacterId = null,
        long victimShipTypeId = 587)
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
                                  {victimShipTypeId},
                                  2,
                                  {(isSolo ? "TRUE" : "FALSE")},
                                  FALSE,
                                  {(isQualifying ? "TRUE" : "FALSE")},
                                  '{DateTimeOffset.UtcNow.UtcDateTime:O}'
                              );
                              """;
        command.ExecuteNonQuery();
    }

    private static void InsertAttacker(KillRightDatabase database, long killmailId, long characterId, long? weaponTypeId = null)
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
                                  ship_type_id,
                                  weapon_type_id
                              ) VALUES (
                                  {killmailId},
                                  {characterId},
                                  98000001,
                                  NULL,
                                  11567,
                                  {(weaponTypeId?.ToString() ?? "NULL")}
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
