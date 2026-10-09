using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Killright.Shared.zKill;
using Killright.Storage.Database;
using Killright.Storage.Killmails;
using Xunit;

namespace Killright.Storage.Tests;

public sealed class RecentKillmailCacheTests
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

    [Fact]
    public async Task RemoveExpiredAsync_FourHundredAndOneCandidates_RunsThreeBatchesAndDeletesAll()
    {
        PurgePassStats? stats = null;
        var (database, cache) = CreateCache(recentWindowDays: 14, passCompleted: value => stats = value);

        InsertNonQualifyingKillmails(database, 720001, 401, DateTimeOffset.UtcNow.AddDays(-20));

        await cache.RemoveExpiredAsync();

        Assert.NotNull(stats);
        Assert.Equal(401, stats!.Candidates);
        Assert.Equal(3, stats.Batches);
        Assert.Equal(0, CountKillmails(database));
        Assert.Equal(0, CountAllAttackers(database));
    }

    [Fact]
    public async Task RemoveExpiredAsync_NoCandidates_ReportsZeroBatches()
    {
        PurgePassStats? stats = null;
        var (database, cache) = CreateCache(recentWindowDays: 14, passCompleted: value => stats = value);

        InsertKillmail(database, 720101, DateTimeOffset.UtcNow.AddDays(-5), isQualifying: false);

        await cache.RemoveExpiredAsync();

        Assert.NotNull(stats);
        Assert.Equal(0, stats!.Candidates);
        Assert.Equal(0, stats.Batches);
        Assert.True(KillmailExists(database, 720101));
    }

    [Fact]
    public async Task RemoveExpiredAsync_CandidateBecomesQualifyingBeforeItsBatch_IsKept()
    {
        KillRightDatabase database = null!;
        RecentKillmailCache cache;

        (database, cache) = CreateCache(
            recentWindowDays: 14,
            beforeBatch: _ =>
            {
                using var connection = database.OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE main.zkill_killmails SET is_qualifying = 1 WHERE killmail_id = 730002;";
                command.ExecuteNonQuery();
            });

        InsertNonQualifyingKillmails(database, 730001, 3, DateTimeOffset.UtcNow.AddDays(-20));

        await cache.RemoveExpiredAsync();

        Assert.False(KillmailExists(database, 730001));
        Assert.True(KillmailExists(database, 730002));
        Assert.False(KillmailExists(database, 730003));
        Assert.Equal(1, CountAttackers(database, 730002));
    }

    [Fact]
    public async Task RemoveExpiredAsync_CandidateBecomesPilotsLatestKillmailBeforeItsBatch_IsKept()
    {
        KillRightDatabase database = null!;
        RecentKillmailCache cache;

        (database, cache) = CreateCache(
            recentWindowDays: 14,
            beforeBatch: _ =>
            {
                InsertActivityCache(database, ScannedCharacterId);
                InsertAttacker(database, 730011, ScannedCharacterId);
            });

        InsertKillmail(database, 730011, DateTimeOffset.UtcNow.AddDays(-20), isQualifying: false);
        InsertAttacker(database, 730011, OtherCharacterId);
        InsertKillmail(database, 730012, DateTimeOffset.UtcNow.AddDays(-21), isQualifying: false);
        InsertAttacker(database, 730012, OtherCharacterId);

        await cache.RemoveExpiredAsync();

        Assert.True(KillmailExists(database, 730011));
        Assert.Equal(2, CountAttackers(database, 730011));
        Assert.False(KillmailExists(database, 730012));
    }

    [Fact]
    public async Task RemoveExpiredAsync_DeletedKillmails_RemoveOnlyTheirOwnAttackerRows()
    {
        var (database, cache) = CreateCache(recentWindowDays: 14);

        InsertKillmail(database, 730021, DateTimeOffset.UtcNow.AddDays(-20), isQualifying: false);
        InsertAttacker(database, 730021, OtherCharacterId);
        InsertAttacker(database, 730021, ScannedCharacterId);
        InsertKillmail(database, 730022, DateTimeOffset.UtcNow.AddDays(-20), isQualifying: true);
        InsertAttacker(database, 730022, OtherCharacterId);
        InsertAttacker(database, 730022, ScannedCharacterId);
        InsertKillmail(database, 730023, DateTimeOffset.UtcNow.AddDays(-2), isQualifying: false);
        InsertAttacker(database, 730023, OtherCharacterId);

        await cache.RemoveExpiredAsync();

        Assert.Equal(0, CountAttackers(database, 730021));
        Assert.Equal(2, CountAttackers(database, 730022));
        Assert.Equal(1, CountAttackers(database, 730023));
        Assert.Equal(3, CountAllAttackers(database));
    }

    [Fact]
    public async Task RemoveExpiredAsync_WriteStartedDuringPass_WaitsForAtMostOneBatch()
    {
        using var firstBatchEntered = new ManualResetEventSlim();

        var (database, cache) = CreateCache(
            recentWindowDays: 14,
            beforeBatch: _ => Thread.Sleep(50),
            insideBatch: _ =>
            {
                firstBatchEntered.Set();
                Thread.Sleep(400);
            });

        InsertNonQualifyingKillmails(database, 740001, 401, DateTimeOffset.UtcNow.AddDays(-20));

        var pass = Task.Run(() => cache.RemoveExpiredAsync());

        Assert.True(firstBatchEntered.Wait(TimeSpan.FromSeconds(10)));

        var stopwatch = Stopwatch.StartNew();

        using (database.BeginWrite())
        {
        }

        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds < 800, $"Write waited {stopwatch.ElapsedMilliseconds} ms.");
        Assert.False(pass.IsCompleted);

        await pass;
    }

    [Fact]
    public async Task RemoveExpiredAsync_CancelledAfterFirstBatch_LeavesRemainingCandidatesUntouched()
    {
        using var cancellation = new CancellationTokenSource();

        var (database, cache) = CreateCache(
            recentWindowDays: 14,
            insideBatch: index =>
            {
                if (index == 0)
                    cancellation.Cancel();
            });

        InsertNonQualifyingKillmails(database, 750001, 401, DateTimeOffset.UtcNow.AddDays(-20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.RemoveExpiredAsync(cancellation.Token));

        Assert.False(KillmailExists(database, 750001));
        Assert.False(KillmailExists(database, 750200));
        Assert.True(KillmailExists(database, 750201));
        Assert.True(KillmailExists(database, 750401));
        Assert.Equal(201, CountKillmails(database));
        Assert.Equal(201, CountAllAttackers(database));
    }

    [Fact]
    public async Task RemoveExpiredAsync_AfterLastBatch_VacuumReducesFreelist()
    {
        KillRightDatabase database = null!;
        RecentKillmailCache cache;
        long freelistBeforeLastBatch = -1;

        (database, cache) = CreateCache(
            recentWindowDays: 14,
            beforeBatch: index =>
            {
                if (index == 2)
                    freelistBeforeLastBatch = FreelistCount(database);
            });

        InsertNonQualifyingKillmails(database, 760001, 401, DateTimeOffset.UtcNow.AddDays(-20));

        await cache.RemoveExpiredAsync();

        Assert.True(freelistBeforeLastBatch > 0);
        Assert.True(FreelistCount(database) < freelistBeforeLastBatch);
    }

    private static (KillRightDatabase Database, RecentKillmailCache Cache) CreateCache(
        int recentWindowDays,
        Action<PurgePassStats>? passCompleted = null,
        Action<int>? beforeBatch = null,
        Action<int>? insideBatch = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"recentKillmailCache.{Guid.NewGuid():N}.db");
        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = path });
        database.EnsureCreated();

        return (database, new RecentKillmailCache(database, recentWindowDays, passCompleted)
        {
            BeforeBatch = beforeBatch,
            InsideBatch = insideBatch
        });
    }

    private static void InsertNonQualifyingKillmails(
        KillRightDatabase database,
        long firstKillmailId,
        int count,
        DateTimeOffset killTimeUtc)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();

        for (var offset = 0; offset < count; offset++)
        {
            var killmailId = firstKillmailId + offset;

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                                  INSERT INTO main.zkill_killmails (
                                      killmail_id, killmail_hash, kill_time_utc, system_id, location_id,
                                      victim_character_id, victim_ship_type_id, unique_attacker_count,
                                      is_solo, is_npc, is_qualifying, cached_at_utc
                                  ) VALUES (
                                      {killmailId}, 'hash{killmailId}', {killTimeUtc.ToUnixTimeSeconds()}, 30000142, 40000001,
                                      NULL, 587, 1,
                                      0, 0, 0, {DateTimeOffset.UtcNow.ToUnixTimeSeconds()}
                                  );
                                  INSERT INTO main.zkill_killmail_attackers (
                                      killmail_id, character_id, corporation_id, alliance_id, ship_type_id, weapon_type_id
                                  ) VALUES (
                                      {killmailId}, {OtherCharacterId}, 98000001, NULL, 11567, NULL
                                  );
                                  """;
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static long FreelistCount(KillRightDatabase database)
    {
        using var connection = database.OpenConnection();

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA freelist_count;";

        return Convert.ToInt64(command.ExecuteScalar()!);
    }

    private static int CountKillmails(KillRightDatabase database)
    {
        using var connection = database.OpenConnection();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM main.zkill_killmails;";

        return Convert.ToInt32(command.ExecuteScalar()!);
    }

    private static int CountAllAttackers(KillRightDatabase database)
    {
        using var connection = database.OpenConnection();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM main.zkill_killmail_attackers;";

        return Convert.ToInt32(command.ExecuteScalar()!);
    }

    private static void InsertActivityCache(KillRightDatabase database, long characterId)
    {
        using var connection = database.OpenConnection();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
                              INSERT INTO main.zkill_activity_cache (
                                  character_id,
                                  has_public_activity_data,
                                  checked_at_utc
                              ) VALUES (
                                  {characterId},
                                  1,
                                  {DateTimeOffset.UtcNow.ToUnixTimeSeconds()}
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
        using var connection = database.OpenConnection();

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
                                  {killTimeUtc.ToUnixTimeSeconds()},
                                  30000142,
                                  40000001,
                                  {(victimCharacterId?.ToString() ?? "NULL")},
                                  {victimShipTypeId},
                                  2,
                                  {(isSolo ? "1" : "0")},
                                  0,
                                  {(isQualifying ? "1" : "0")},
                                  {DateTimeOffset.UtcNow.ToUnixTimeSeconds()}
                              );
                              """;
        command.ExecuteNonQuery();
    }

    private static void InsertAttacker(KillRightDatabase database, long killmailId, long characterId, long? weaponTypeId = null)
    {
        using var connection = database.OpenConnection();

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
        using var connection = database.OpenConnection();

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM main.zkill_killmails WHERE killmail_id = {killmailId};";

        return Convert.ToInt64(command.ExecuteScalar()!) > 0;
    }

    private static int CountAttackers(KillRightDatabase database, long killmailId)
    {
        using var connection = database.OpenConnection();

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM main.zkill_killmail_attackers WHERE killmail_id = {killmailId};";

        return Convert.ToInt32(command.ExecuteScalar()!);
    }
}
