using DuckDB.NET.Data;
using Killright.Integration.zKill;
using Killright.Shared;
using Killright.Shared.Killmails;
using Killright.Shared.zKill;
using Killright.Storage.Database;
using Killright.Storage.Identity;
using Killright.Storage.Scan;
using Killright.Storage.zKill;
using Xunit;

namespace Killright.Storage.Tests;

public sealed class ScanWriterTests
{
    private const long Lukas = 95465499;
    private const long Tral = 91321792;
    private const long Symptom = 2112625428;
    private const int Threshold = 11;
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Commit_WritesEveryTableInOneTransaction_AndReadsBackThroughTheCaches()
    {
        var database = CreateDatabase();
        var batch = new ScanWriteBatch();
        batch.AddIdentity(Identity("LUKAS NAARII", Lukas));
        batch.AddEntityNames([new EsiEntityName(98000001, EsiEntityTypes.Corporation, "Test Corp")]);
        batch.AddStatistics(new PendingStatistics(Lukas, Statistics(), "Gang", false, Now));
        batch.AddKillmails(0, Lukas, [Killmail(1, Lukas, Tral)]);
        batch.AddActivity(new zKillActivity(Lukas, true, 3, 1, Now, zKillActivityType.Kill, Now, null, Now, Now.AddDays(-7)));

        using (var session = database.OpenScanSession())
            ScanWriter.Commit(session, batch, Threshold, Now);

        var identity = await new DuckDbPilotIdentityCache(database).GetRecordAsync("Lukas Naarii");
        var names = await new DuckDbEsiEntityNameCache(database).GetNamesAsync([98000001]);
        var statistics = await new DuckDbzKillStatisticsCache(database).GetAsync(Lukas, TimeSpan.FromDays(30));
        var activity = await new DuckDbzKillActivityCache(database).GetAsync(Lukas);

        Assert.Equal(Lukas, identity!.CharacterId);
        Assert.Equal("Test Corp", names[98000001]);
        Assert.Equal(120, statistics!.shipsDestroyed);
        Assert.Equal(3, activity!.KillsWeek);
        Assert.Equal(zKillActivityType.Kill, activity.LastActivityType);
        Assert.Equal(1, CountRows(database, "zkill_killmails"));
        Assert.Equal(2, CountRows(database, "zkill_killmail_attackers"));
    }

    [Fact]
    public async Task Commit_ExistingRows_AreReplaced()
    {
        var database = CreateDatabase();
        var first = new ScanWriteBatch();
        first.AddIdentity(Identity("LUKAS NAARII", Lukas, corporationId: 1));
        first.AddStatistics(new PendingStatistics(Lukas, Statistics(shipsDestroyed: 10), "Solo", true, Now));
        var second = new ScanWriteBatch();
        second.AddIdentity(Identity("LUKAS NAARII", Lukas, corporationId: 2));
        second.AddStatistics(new PendingStatistics(Lukas, Statistics(shipsDestroyed: 20), "Gang", false, Now));

        using (var session = database.OpenScanSession())
        {
            ScanWriter.Commit(session, first, Threshold, Now);
            ScanWriter.Commit(session, second, Threshold, Now);
        }

        var identity = await new DuckDbPilotIdentityCache(database).GetRecordAsync("Lukas Naarii");
        var statistics = await new DuckDbzKillStatisticsCache(database).GetAsync(Lukas, TimeSpan.FromDays(30));

        Assert.Equal(2, identity!.CorporationId);
        Assert.Equal(20, statistics!.shipsDestroyed);
        Assert.False(statistics.NoHistory);
        Assert.Equal(1, CountRows(database, "pilot_identity_cache"));
        Assert.Equal(1, CountRows(database, "zkill_statistics_cache"));
    }

    [Fact]
    public async Task Commit_NoHistoryClear_ResetsTheMarkerAfterTheStatisticsWrite()
    {
        var database = CreateDatabase();
        var batch = new ScanWriteBatch();
        batch.AddStatistics(new PendingStatistics(Lukas, new zKillStatistics { NoHistory = true }, "Unk", true, Now));
        batch.AddNoHistoryClear(Lukas);

        using (var session = database.OpenScanSession())
            ScanWriter.Commit(session, batch, Threshold, Now);

        var statistics = await new DuckDbzKillStatisticsCache(database).GetAsync(Lukas, TimeSpan.FromDays(30));

        Assert.False(statistics!.NoHistory);
    }

    [Fact]
    public void Commit_FailureRollsBackEveryWriteOfTheScan()
    {
        var database = CreateDatabase();
        var batch = new ScanWriteBatch();
        batch.AddIdentity(Identity("LUKAS NAARII", Lukas));
        batch.AddStatistics(new PendingStatistics(Lukas, Statistics(), "Gang", false, Now));
        batch.AddKillmails(0, Lukas, [Killmail(1, Lukas, Tral)]);

        using (var connection = database.OpenConnection())
        {
            using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE main.zkill_killmail_attackers;";
            command.ExecuteNonQuery();
        }

        using (var session = database.OpenScanSession())
            Assert.ThrowsAny<Exception>(() => ScanWriter.Commit(session, batch, Threshold, Now));

        Assert.Equal(0, CountRows(database, "pilot_identity_cache"));
        Assert.Equal(0, CountRows(database, "zkill_statistics_cache"));
        Assert.Equal(0, CountRows(database, "zkill_killmails"));
    }

    [Fact]
    public void Commit_EmptyBatch_WritesNothing()
    {
        var database = CreateDatabase();

        using (var session = database.OpenScanSession())
            ScanWriter.Commit(session, new ScanWriteBatch(), Threshold, Now);

        Assert.Equal(0, CountRows(database, "zkill_killmails"));
    }

    [Fact]
    public void Commit_SharedKillmailAcrossPilots_WritesOneKillmailRowAndTheUnionOfAttackers()
    {
        var database = CreateDatabase();
        var shared = Killmail(10, Lukas, Tral);
        var batch = new ScanWriteBatch();
        batch.AddKillmails(1, Tral, [shared]);
        batch.AddKillmails(0, Lukas, [shared]);

        using (var session = database.OpenScanSession())
            ScanWriter.Commit(session, batch, Threshold, Now);

        Assert.Equal(1, CountRows(database, "zkill_killmails"));
        Assert.Equal(2, CountRows(database, "zkill_killmail_attackers"));
    }

    [Fact]
    public void Commit_NonQualifyingKillmailSeenByTwoPilots_KeepsOnlyEachPilotsOwnAttackerRow()
    {
        var database = CreateDatabase();
        var seenByLukas = Killmail(20, Lukas);
        var seenByTral = seenByLukas with { Attackers = [new KillmailAttacker(Tral, 98000002, null, 17738)] };
        var batch = new ScanWriteBatch();
        batch.AddKillmails(0, Lukas, [seenByLukas]);
        batch.AddKillmails(1, Tral, [seenByTral]);

        using (var session = database.OpenScanSession())
            ScanWriter.Commit(session, batch, Threshold, Now);

        Assert.False(ReadIsQualifying(database, 20));
        Assert.Equal(2, CountRows(database, "zkill_killmail_attackers"));
    }

    [Fact]
    public void Commit_QualifyingEncounterAfterNonQualifying_FlipsTheFlagAndAddsAllAttackers()
    {
        var database = CreateDatabase();
        var nonQualifying = Killmail(30, Lukas);
        var qualifying = Killmail(30, Lukas, Tral, Symptom);
        var batch = new ScanWriteBatch();
        batch.AddKillmails(0, Lukas, [nonQualifying]);
        batch.AddKillmails(1, Tral, [qualifying]);

        using (var session = database.OpenScanSession())
            ScanWriter.Commit(session, batch, Threshold, Now);

        Assert.True(ReadIsQualifying(database, 30));
        Assert.Equal(3, CountRows(database, "zkill_killmail_attackers"));
    }

    [Fact]
    public void Commit_KillmailAlreadyStored_SkipsTheKillmailRowAndAddsMissingAttackers()
    {
        var database = CreateDatabase();
        var first = new ScanWriteBatch();
        first.AddKillmails(0, Lukas, [Killmail(40, Lukas, Tral)]);
        var second = new ScanWriteBatch();
        second.AddKillmails(0, Symptom, [Killmail(40, Lukas, Tral, Symptom)]);

        using (var session = database.OpenScanSession())
        {
            ScanWriter.Commit(session, first, Threshold, Now);
            ScanWriter.Commit(session, second, Threshold, Now.AddHours(1));
        }

        Assert.Equal(1, CountRows(database, "zkill_killmails"));
        Assert.Equal(3, CountRows(database, "zkill_killmail_attackers"));
        Assert.Equal(Now.UtcDateTime, ReadCachedAt(database, 40).UtcDateTime);
    }

    [Fact]
    public void Commit_KillmailsBeyondOneChunk_AreAllWritten()
    {
        var database = CreateDatabase();
        var killmails = Enumerable.Range(1000, 1200).Select(id => Killmail(id, Lukas, Tral)).ToList();
        var batch = new ScanWriteBatch();
        batch.AddKillmails(0, Lukas, killmails);

        using (var session = database.OpenScanSession())
            ScanWriter.Commit(session, batch, Threshold, Now);

        Assert.Equal(1200, CountRows(database, "zkill_killmails"));
        Assert.Equal(2400, CountRows(database, "zkill_killmail_attackers"));
    }

    [Fact]
    public async Task BatchedReads_ThroughOneSession_OpenExactlyOneConnection()
    {
        var database = CreateDatabase();
        var seed = new ScanWriteBatch();
        seed.AddIdentity(Identity("LUKAS NAARII", Lukas));
        seed.AddEntityNames([new EsiEntityName(98000001, EsiEntityTypes.Corporation, "Test Corp")]);
        seed.AddStatistics(new PendingStatistics(Lukas, Statistics(), "Gang", false, DateTimeOffset.UtcNow));
        seed.AddActivity(new zKillActivity(Lukas, true, 3, 1, Now, zKillActivityType.Kill, Now));

        using (var seedSession = database.OpenScanSession())
            ScanWriter.Commit(seedSession, seed, Threshold, Now);

        var before = database.ConnectionsOpened;
        var write = new ScanWriteBatch();
        write.AddKillmails(0, Lukas, [Killmail(50, Lukas, Tral)]);

        using (var session = database.OpenScanSession())
        {
            var identities = await new DuckDbPilotIdentityCache(database).GetRecordsAsync(["Lukas Naarii", "Nobody"], session);
            var names = await new DuckDbEsiEntityNameCache(database).GetNamesAsync([98000001], session);
            var statistics = await new DuckDbzKillStatisticsCache(database).GetManyAsync([Lukas, Tral], TimeSpan.FromDays(30), session);
            var activities = await new DuckDbzKillActivityCache(database).GetManyAsync([Lukas, Tral], session);
            ScanWriter.Commit(session, write, Threshold, Now);

            Assert.Single(identities);
            Assert.Single(names);
            Assert.Single(statistics);
            Assert.Single(activities);
        }

        Assert.Equal(1, database.ConnectionsOpened - before);
    }

    [Fact]
    public async Task GetRecordsAsync_MatchesPerNameReadsAndKeysByNormalizedName()
    {
        var database = CreateDatabase();
        var cache = new DuckDbPilotIdentityCache(database);
        await cache.UpsertRecordAsync(Identity("LUKAS NAARII", Lukas, birthday: new DateOnly(2015, 6, 12)));
        await cache.UpsertRecordAsync(Identity("T'RAL VSENGNE", Tral));

        var batch = await cache.GetRecordsAsync(["  lukas naarii ", "T'ral Vsengne", "Missing Pilot"]);
        var lukas = await cache.GetRecordAsync("Lukas Naarii");
        var tral = await cache.GetRecordAsync("T'ral Vsengne");

        Assert.Equal(2, batch.Count);
        Assert.Equal(lukas!.CharacterId, batch["LUKAS NAARII"].CharacterId);
        Assert.Equal(lukas.Birthday, batch["LUKAS NAARII"].Birthday);
        Assert.Equal(lukas.SecurityStatusAtUtc, batch["LUKAS NAARII"].SecurityStatusAtUtc);
        Assert.Equal(tral!.CharacterId, batch["T'RAL VSENGNE"].CharacterId);
    }

    [Fact]
    public async Task GetManyAsync_Statistics_HonoursMaximumAgeAndMonthsProcessed()
    {
        var database = CreateDatabase();
        var cache = new DuckDbzKillStatisticsCache(database);
        await cache.UpsertAsync(Lukas, Statistics(), "Gang", false);
        await cache.UpsertAsync(Tral, Statistics(shipsDestroyed: 5), "Solo", false);

        using (var connection = database.OpenConnection())
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"UPDATE main.zkill_statistics_cache SET checked_at_utc = '2020-01-01T00:00:00.0000000Z' WHERE character_id = {Tral};";
            command.ExecuteNonQuery();
        }

        var fresh = await cache.GetManyAsync([Lukas, Tral, Symptom], TimeSpan.FromDays(30));
        var single = await cache.GetAsync(Lukas, TimeSpan.FromDays(30));

        Assert.Single(fresh);
        Assert.Equal(single!.shipsDestroyed, fresh[Lukas].shipsDestroyed);
        Assert.Null(await cache.GetAsync(Tral, TimeSpan.FromDays(30)));
    }

    [Fact]
    public async Task Commit_ActivityWrite_PersistsLastKillUtcAndASecondWriteReplacesIt()
    {
        var database = CreateDatabase();
        var first = new ScanWriteBatch();
        first.AddActivity(new zKillActivity(Lukas, true, 0, 0, Now, zKillActivityType.Loss, Now, LastKillUtc: Now.AddDays(-40)));
        var second = new ScanWriteBatch();
        second.AddActivity(new zKillActivity(Lukas, true, 0, 0, Now, zKillActivityType.Loss, Now, LastKillUtc: Now.AddDays(-10)));
        var cache = new DuckDbzKillActivityCache(database);

        using (var session = database.OpenScanSession())
            ScanWriter.Commit(session, first, Threshold, Now);

        Assert.Equal(Now.AddDays(-40), (await cache.GetAsync(Lukas))!.LastKillUtc);

        using (var session = database.OpenScanSession())
            ScanWriter.Commit(session, second, Threshold, Now);

        Assert.Equal(Now.AddDays(-10), (await cache.GetAsync(Lukas))!.LastKillUtc);
        Assert.Equal(1, CountRows(database, "zkill_activity_cache"));
    }

    [Fact]
    public async Task EnsureCreated_ActivityTableWithoutLastKillColumn_GainsTheColumnAndKeepsItsRows()
    {
        var database = CreateDatabase();

        using (var connection = database.OpenConnection())
        {
            using var drop = connection.CreateCommand();
            drop.CommandText = "ALTER TABLE main.zkill_activity_cache DROP COLUMN last_kill_utc;";
            drop.ExecuteNonQuery();

            using var insert = connection.CreateCommand();
            insert.CommandText = $"INSERT INTO main.zkill_activity_cache (character_id, has_public_activity_data, checked_at_utc) VALUES ({Lukas}, TRUE, '2026-09-30T12:00:00.0000000Z');";
            insert.ExecuteNonQuery();
        }

        database.EnsureCreated();

        var activity = await new DuckDbzKillActivityCache(database).GetAsync(Lukas);

        Assert.NotNull(activity);
        Assert.Null(activity!.LastKillUtc);
        Assert.Equal(1, CountRows(database, "zkill_activity_cache"));
    }

    [Fact]
    public async Task GetManyAsync_Activity_MatchesPerPilotReads()
    {
        var database = CreateDatabase();
        var cache = new DuckDbzKillActivityCache(database);
        await cache.UpsertAsync(new zKillActivity(Lukas, true, 3, 1, Now, zKillActivityType.Loss, Now, null, Now, Now.AddDays(-7)));

        var many = await cache.GetManyAsync([Lukas, Tral]);
        var single = await cache.GetAsync(Lukas);

        Assert.Single(many);
        Assert.Equal(single, many[Lukas]);
    }

    private static KillRightDatabase CreateDatabase()
    {
        var path = Path.Combine(Path.GetTempPath(), $"scanWriter.{Guid.NewGuid():N}.duckdb");
        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = path });
        database.EnsureCreated();

        return database;
    }

    private static PilotIdentityCacheRecord Identity(
        string inputName,
        long characterId,
        long corporationId = 98000001,
        DateOnly? birthday = null)
    {
        return new PilotIdentityCacheRecord
        {
            InputName = inputName,
            CharacterId = characterId,
            CharacterName = inputName,
            VerifyStatus = VerifyStatus.Partial,
            SecurityStatus = -1.2,
            CorporationId = corporationId,
            AllianceId = 99001,
            Birthday = birthday ?? new DateOnly(2015, 6, 12),
            SecurityStatusAtUtc = new DateTime(2026, 9, 30, 11, 0, 0, DateTimeKind.Utc),
            CachedAtUtc = new DateTime(2026, 9, 30, 11, 0, 0, DateTimeKind.Utc)
        };
    }

    private static zKillStatistics Statistics(int shipsDestroyed = 120)
    {
        return new zKillStatistics
        {
            shipsDestroyed = shipsDestroyed,
            soloKills = 30,
            soloRatio = 0.25,
            avgGangSize = 4.0,
            shipsLost = 12,
            soloLosses = 2,
            podKills = 1
        };
    }

    private static RawKillmail Killmail(long killmailId, params long[] attackerIds)
    {
        return new RawKillmail(
            killmailId,
            $"hash{killmailId}",
            new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.Zero),
            30000142,
            40000001,
            999,
            587,
            attackerIds.Length == 1,
            false,
            attackerIds.Select(id => new KillmailAttacker(id, 98000001, null, 11567)).ToList());
    }

    private static int CountRows(KillRightDatabase database, string table)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM main.{table};";

        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static bool ReadIsQualifying(KillRightDatabase database, long killmailId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT is_qualifying FROM main.zkill_killmails WHERE killmail_id = {killmailId};";

        return Convert.ToBoolean(command.ExecuteScalar());
    }

    private static DateTimeOffset ReadCachedAt(KillRightDatabase database, long killmailId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT cached_at_utc FROM main.zkill_killmails WHERE killmail_id = {killmailId};";

        return DateTimeOffset.Parse(Convert.ToString(command.ExecuteScalar())!, null, System.Globalization.DateTimeStyles.AssumeUniversal);
    }
}
