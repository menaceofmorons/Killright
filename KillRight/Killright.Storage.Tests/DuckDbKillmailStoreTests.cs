using DuckDB.NET.Data;
using Killright.Shared.Killmails;
using Killright.Storage.Database;
using Killright.Storage.Killmails;
using Xunit;

namespace Killright.Storage.Tests;

public sealed class DuckDbKillmailStoreTests
{
    private const long ScannedCharacterId = 95465499;

    [Fact]
    public async Task UpsertAsync_QualifyingKillmail_WritesKillmailAndAllAttackerRows()
    {
        var (database, store) = CreateStore();

        var killmail = new RawKillmail(
            123456,
            "abc123",
            new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.Zero),
            30000142,
            40000001,
            999,
            587,
            false,
            false,
            [
                new KillmailAttacker(ScannedCharacterId, 98000001, null, 11567),
                new KillmailAttacker(91321792, 98000002, 99000001, 17738)
            ]);

        await store.UpsertAsync(ScannedCharacterId, [killmail]);

        var (uniqueAttackerCount, isQualifying) = ReadKillmail(database, 123456);
        Assert.Equal(2, uniqueAttackerCount);
        Assert.True(isQualifying);

        var attackerCount = CountAttackers(database, 123456);
        Assert.Equal(2, attackerCount);
    }

    [Fact]
    public async Task UpsertAsync_NonQualifyingKillmail_WritesOnlyScannedPilotAttackerRow()
    {
        var (database, store) = CreateStore();

        var killmail = new RawKillmail(
            223456,
            "def456",
            new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.Zero),
            30000142,
            40000001,
            999,
            587,
            true,
            false,
            [
                new KillmailAttacker(ScannedCharacterId, 98000001, null, 11567)
            ]);

        await store.UpsertAsync(ScannedCharacterId, [killmail]);

        var (uniqueAttackerCount, isQualifying) = ReadKillmail(database, 223456);
        Assert.Equal(1, uniqueAttackerCount);
        Assert.False(isQualifying);

        var attackerCount = CountAttackers(database, 223456);
        Assert.Equal(1, attackerCount);
    }

    [Fact]
    public async Task UpsertAsync_KillmailAlreadyStored_SkipsKillmailRowButAddsMissingAttackerRows()
    {
        var (database, store) = CreateStore();

        var firstSeen = new RawKillmail(
            323456,
            "ghi789",
            new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.Zero),
            30000142,
            40000001,
            999,
            587,
            false,
            false,
            [
                new KillmailAttacker(ScannedCharacterId, 98000001, null, 11567),
                new KillmailAttacker(91321792, 98000002, 99000001, 17738)
            ]);

        await store.UpsertAsync(ScannedCharacterId, [firstSeen]);

        var secondScannerId = 91321792L;
        var seenAgainWithExtraAttacker = firstSeen with
        {
            Attackers =
            [
                .. firstSeen.Attackers,
                new KillmailAttacker(90000003, 98000003, null, 670)
            ]
        };

        await store.UpsertAsync(secondScannerId, [seenAgainWithExtraAttacker]);

        Assert.Equal(3, CountAttackers(database, 323456));

        var cachedAtValues = ReadDistinctCachedAtUtc(database, 323456);
        Assert.Single(cachedAtValues);
    }

    [Fact]
    public async Task UpsertAsync_QualifyingFlagFlipsFalseToTrue_WritesFullAttackerRowsFromPayload()
    {
        var (database, store) = CreateStore();

        var nonQualifying = new RawKillmail(
            423456,
            "jkl012",
            new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.Zero),
            30000142,
            40000001,
            999,
            587,
            false,
            false,
            [
                new KillmailAttacker(ScannedCharacterId, 98000001, null, 11567)
            ]);

        await store.UpsertAsync(ScannedCharacterId, [nonQualifying]);

        var (_, isQualifyingBefore) = ReadKillmail(database, 423456);
        Assert.False(isQualifyingBefore);

        var nowQualifying = nonQualifying with
        {
            Attackers =
            [
                new KillmailAttacker(ScannedCharacterId, 98000001, null, 11567),
                new KillmailAttacker(91321792, 98000002, 99000001, 17738)
            ]
        };

        await store.UpsertAsync(ScannedCharacterId, [nowQualifying]);

        var (uniqueAttackerCount, isQualifyingAfter) = ReadKillmail(database, 423456);
        Assert.Equal(1, uniqueAttackerCount); // killmail row itself is skipped on re-see, only is_qualifying + attacker rows change
        Assert.True(isQualifyingAfter);
        Assert.Equal(2, CountAttackers(database, 423456));
    }

    [Fact]
    public async Task UpsertAsync_PodKill_NeverQualifiesRegardlessOfAttackerCount()
    {
        var (database, store) = CreateStore();

        var podKill = new RawKillmail(
            523456,
            "mno345",
            new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.Zero),
            30000142,
            40000001,
            999,
            670,
            false,
            false,
            [
                new KillmailAttacker(ScannedCharacterId, 98000001, null, 11567),
                new KillmailAttacker(91321792, 98000002, 99000001, 17738)
            ]);

        await store.UpsertAsync(ScannedCharacterId, [podKill]);

        var (_, isQualifying) = ReadKillmail(database, 523456);
        Assert.False(isQualifying);
    }

    [Fact]
    public async Task UpsertAsync_EmptyList_NoOp()
    {
        var (_, store) = CreateStore();

        await store.UpsertAsync(ScannedCharacterId, []);
    }

    private static (KillRightDatabase Database, DuckDbKillmailStore Store) CreateStore()
    {
        var path = Path.Combine(Path.GetTempPath(), $"killmailStore.{Guid.NewGuid():N}.duckdb");
        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = path });
        database.EnsureCreated();
        return (database, new DuckDbKillmailStore(database));
    }

    private static (int UniqueAttackerCount, bool IsQualifying) ReadKillmail(KillRightDatabase database, long killmailId)
    {
        using var connection = new DuckDBConnection(database.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
                              SELECT unique_attacker_count, is_qualifying
                              FROM main.zkill_killmails
                              WHERE killmail_id = {killmailId};
                              """;

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());

        return (reader.GetInt32(0), reader.GetBoolean(1));
    }

    private static int CountAttackers(KillRightDatabase database, long killmailId)
    {
        using var connection = new DuckDBConnection(database.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
                              SELECT COUNT(*)
                              FROM main.zkill_killmail_attackers
                              WHERE killmail_id = {killmailId};
                              """;

        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static List<string> ReadDistinctCachedAtUtc(KillRightDatabase database, long killmailId)
    {
        using var connection = new DuckDBConnection(database.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
                              SELECT DISTINCT cached_at_utc
                              FROM main.zkill_killmails
                              WHERE killmail_id = {killmailId};
                              """;

        using var reader = command.ExecuteReader();
        var values = new List<string>();

        while (reader.Read())
            values.Add(reader.GetString(0));

        return values;
    }
}
