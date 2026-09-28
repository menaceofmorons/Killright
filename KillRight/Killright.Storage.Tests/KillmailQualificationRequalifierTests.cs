using DuckDB.NET.Data;
using Killright.Shared.Killmails;
using Killright.Storage.Database;
using Killright.Storage.Killmails;
using Xunit;

namespace Killright.Storage.Tests;

public sealed class KillmailQualificationRequalifierTests
{
    private const long ScannedCharacterId = 95465499;

    [Fact]
    public void RequalifyOnStartup_NoStoredValue_StoresConfiguredValueWithoutRecheck()
    {
        var database = CreateDatabase();

        KillmailQualificationRequalifier.RequalifyOnStartup(database, 11);

        Assert.Equal(11, database.GetLastAppliedQualificationFleetThreshold());
    }

    [Fact]
    public async Task RequalifyOnStartup_ConfiguredLowerThanStored_SetsQualifyingFalseAtOrAboveNewThreshold()
    {
        var database = CreateDatabase();
        var store = new DuckDbKillmailStore(database, qualificationFleetThreshold: 11);

        await store.UpsertAsync(ScannedCharacterId, [BuildKillmail(823456, attackerCount: 5)]);
        await store.UpsertAsync(ScannedCharacterId, [BuildKillmail(823457, attackerCount: 2)]);

        KillmailQualificationRequalifier.RequalifyOnStartup(database, 11);
        KillmailQualificationRequalifier.RequalifyOnStartup(database, 4);

        Assert.False(ReadIsQualifying(database, 823456));
        Assert.True(ReadIsQualifying(database, 823457));
        Assert.Equal(4, database.GetLastAppliedQualificationFleetThreshold());
    }

    [Fact]
    public void RequalifyOnStartup_ConfiguredHigherThanStored_StoresNewValueOnly()
    {
        var database = CreateDatabase();

        KillmailQualificationRequalifier.RequalifyOnStartup(database, 8);
        KillmailQualificationRequalifier.RequalifyOnStartup(database, 11);

        Assert.Equal(11, database.GetLastAppliedQualificationFleetThreshold());
    }

    private static KillRightDatabase CreateDatabase()
    {
        var path = Path.Combine(Path.GetTempPath(), $"requalifier.{Guid.NewGuid():N}.duckdb");
        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = path });
        database.EnsureCreated();
        return database;
    }

    private static RawKillmail BuildKillmail(long killmailId, int attackerCount)
    {
        var attackers = new List<KillmailAttacker> { new(ScannedCharacterId, 98000001, null, 11567) };

        for (var i = 1; i < attackerCount; i++)
            attackers.Add(new KillmailAttacker(90000000 + i, 98000002, 99000001, 17738));

        return new RawKillmail(
            killmailId,
            killmailId.ToString(),
            new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.Zero),
            30000142,
            40000001,
            999,
            587,
            false,
            false,
            attackers);
    }

    private static bool ReadIsQualifying(KillRightDatabase database, long killmailId)
    {
        using var connection = new DuckDBConnection(database.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT is_qualifying FROM main.zkill_killmails WHERE killmail_id = {killmailId};";

        return Convert.ToBoolean(command.ExecuteScalar());
    }
}
